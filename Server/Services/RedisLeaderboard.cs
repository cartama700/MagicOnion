using StackExchange.Redis;

namespace Server.Services;

public sealed class RedisLeaderboard : ILeaderboard
{
    private readonly IConnectionMultiplexer _redis;

    public RedisLeaderboard(IConnectionMultiplexer redis) => _redis = redis;

    private static RedisKey Key(string roomId) => $"lb:{roomId}";

    public async Task AddScoreAsync(string roomId, int playerId, double delta, CancellationToken ct = default)
    {
        await _redis.GetDatabase().SortedSetIncrementAsync(Key(roomId), playerId, delta);
    }

    public async Task<IReadOnlyList<(int PlayerId, double Score)>> TopAsync(string roomId, int n, CancellationToken ct = default)
    {
        var entries = await _redis.GetDatabase()
            .SortedSetRangeByRankWithScoresAsync(Key(roomId), 0, n - 1, Order.Descending);
        var result = new List<(int, double)>(entries.Length);
        foreach (var e in entries)
            result.Add(((int)e.Element, e.Score));
        return result;
    }

    public async Task RemoveAsync(string roomId, int playerId, CancellationToken ct = default)
    {
        await _redis.GetDatabase().SortedSetRemoveAsync(Key(roomId), playerId);
    }
}
