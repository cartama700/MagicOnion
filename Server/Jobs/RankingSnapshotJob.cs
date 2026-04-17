using System.Collections.Concurrent;
using Server.Services;

namespace Server.Jobs;

public sealed class RankingSnapshotJob : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(15);

    private readonly ILeaderboard _leaderboard;
    private readonly SnapshotService _snapshot;
    private readonly ConcurrentDictionary<string, IReadOnlyList<(int PlayerId, double Score)>> _latest = new();
    private readonly ILogger<RankingSnapshotJob> _logger;

    public RankingSnapshotJob(ILeaderboard leaderboard, SnapshotService snapshot, ILogger<RankingSnapshotJob> logger)
    {
        _leaderboard = leaderboard;
        _snapshot = snapshot;
        _logger = logger;
    }

    public IReadOnlyDictionary<string, IReadOnlyList<(int PlayerId, double Score)>> Latest => _latest;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RankingSnapshotJob started — period {PeriodSec}s", Period.TotalSeconds);
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                foreach (var (room, _) in _snapshot.RoomList())
                {
                    var top = await _leaderboard.TopAsync(room, 10, stoppingToken);
                    _latest[room] = top;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RankingSnapshotJob iteration failed");
            }
        }
    }
}
