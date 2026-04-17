using MagicOnion.Server.Hubs;
using Server.Persistence;
using Server.Services;
using Shared;

namespace Server.Hubs;

// Phase 11: Hub 에서 DB 동기 INSERT 제거. Close 시점에 한 번 Channel 로 enqueue.

public sealed class MovementHub : StreamingHubBase<IMovementHub, IMovementHubReceiver>, IMovementHub
{
    private const int LeaderboardSampleEvery = 10; // 10 moves → 1 score increment (10 → 1Hz at 100ms tick)

    private readonly MetricsService _metrics;
    private readonly SnapshotService _snapshot;
    private readonly OptimizationMode _optimization;
    private readonly ILeaderboard _leaderboard;
    private readonly IPlayerRepository _players;
    private readonly MatchWriteQueue _matchQueue;
    private readonly LatencyHistogram _latency;
    private readonly ILogger<MovementHub> _logger;

    private IGroup<IMovementHubReceiver>? _room;
    private string _roomId = "world";
    private int _playerId;
    private Guid _matchId;
    private DateTime _joinedAtUtc;
    private long _moveCount;
    private long _scoreLocal;

    public MovementHub(
        MetricsService metrics,
        SnapshotService snapshot,
        OptimizationMode optimization,
        ILeaderboard leaderboard,
        IPlayerRepository players,
        MatchWriteQueue matchQueue,
        LatencyHistogram latency,
        ILogger<MovementHub> logger)
    {
        _metrics = metrics;
        _snapshot = snapshot;
        _optimization = optimization;
        _leaderboard = leaderboard;
        _players = players;
        _matchQueue = matchQueue;
        _latency = latency;
        _logger = logger;
    }

    public async ValueTask JoinAsync(int playerId, string roomId, float startX, float startY)
    {
        _playerId = playerId;
        _roomId = string.IsNullOrEmpty(roomId) ? "world" : roomId;
        _room = await Group.AddAsync(_roomId);
        _snapshot.Set(_roomId, playerId, startX, startY);
        _metrics.PlayerJoined();

        _matchId = Guid.CreateVersion7();   // time-ordered → BTree locality 유지
        _joinedAtUtc = DateTime.UtcNow;

        // 프로파일 upsert 는 저빈도 (세션당 1회). 실패해도 세션은 진행.
        _ = SafeUpsertProfileAsync(playerId);

        _room.All.OnPlayerJoined(new PlayerMoveDto { PlayerId = playerId, X = startX, Y = startY });
    }

    private async Task SafeUpsertProfileAsync(int playerId)
    {
        try { await _players.UpsertProfileAsync(playerId, $"bot-{playerId}"); }
        catch (Exception ex) { _logger.LogDebug(ex, "UpsertProfile skipped for player {PlayerId}", playerId); }
    }

    public ValueTask MoveAsync(PlayerMoveDto moveData)
    {
        _metrics.IncrementPacket();
        _snapshot.Set(_roomId, moveData.PlayerId, moveData.X, moveData.Y);

        if (moveData.SentAtMs > 0)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var delta = (int)(now - moveData.SentAtMs);
            if (delta >= 0 && delta < 60_000) _latency.Record(delta);
        }

        if (_room is null) return ValueTask.CompletedTask;

        var world = _snapshot.RawRoom(_roomId);
        var hits = _optimization.IsOn
            ? AoiFilter.Optimized(world, moveData.PlayerId, moveData.X, moveData.Y, AoiFilter.DefaultRadius)
            : AoiFilter.Naive(world, moveData.PlayerId, moveData.X, moveData.Y, AoiFilter.DefaultRadius);
        _metrics.RecordAoiHits(hits);

        _moveCount++;
        _scoreLocal++;
        if (_moveCount % LeaderboardSampleEvery == 0)
        {
            _ = _leaderboard.AddScoreAsync(_roomId, moveData.PlayerId, LeaderboardSampleEvery);
        }

        _room.All.OnPlayerMoved(moveData);
        return ValueTask.CompletedTask;
    }

    public async ValueTask LeaveAsync()
    {
        await CleanupAsync();
        _metrics.PlayerLeft();
    }

    protected override async ValueTask OnDisconnected()
    {
        if (_playerId != 0)
        {
            await CleanupAsync();
            _metrics.PlayerLeft();
        }
    }

    private async ValueTask CleanupAsync()
    {
        _snapshot.Remove(_roomId, _playerId);
        if (_room is not null)
        {
            await _room.RemoveAsync(Context);
            _room.All.OnPlayerLeft(_playerId);
        }
        try
        {
            await _leaderboard.RemoveAsync(_roomId, _playerId);
            if (_matchId != Guid.Empty)
            {
                // 메인 스레드는 Channel 에 한 번 TryWrite 만 — DB 는 MatchFlushJob 이 배치 INSERT.
                _matchQueue.TryEnqueue(new MatchRecord(
                    _matchId, _playerId, _roomId, _joinedAtUtc, DateTime.UtcNow, _scoreLocal));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Cleanup queue skipped for player {PlayerId}", _playerId);
        }
    }
}
