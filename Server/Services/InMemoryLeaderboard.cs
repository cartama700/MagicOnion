using System.Collections.Concurrent;

namespace Server.Services;

public sealed class InMemoryLeaderboard : ILeaderboard
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, double>> _rooms = new();

    public Task AddScoreAsync(string roomId, int playerId, double delta, CancellationToken ct = default)
    {
        var room = _rooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<int, double>());
        room.AddOrUpdate(playerId, delta, (_, prev) => prev + delta);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(int PlayerId, double Score)>> TopAsync(string roomId, int n, CancellationToken ct = default)
    {
        if (!_rooms.TryGetValue(roomId, out var room))
            return Task.FromResult<IReadOnlyList<(int, double)>>(Array.Empty<(int, double)>());

        var top = room
            .Select(kv => (kv.Key, kv.Value))
            .OrderByDescending(t => t.Value)
            .Take(n)
            .ToList();
        return Task.FromResult<IReadOnlyList<(int, double)>>(top);
    }

    public Task RemoveAsync(string roomId, int playerId, CancellationToken ct = default)
    {
        if (_rooms.TryGetValue(roomId, out var room)) room.TryRemove(playerId, out _);
        return Task.CompletedTask;
    }
}
