namespace Server.Persistence;

public interface IPlayerRepository
{
    Task UpsertProfileAsync(int playerId, string displayName, CancellationToken ct = default);
    Task BulkInsertMatchesAsync(IReadOnlyList<MatchRecord> records, CancellationToken ct = default);
}

public sealed class NullPlayerRepository : IPlayerRepository
{
    public Task UpsertProfileAsync(int playerId, string displayName, CancellationToken ct = default) => Task.CompletedTask;
    public Task BulkInsertMatchesAsync(IReadOnlyList<MatchRecord> records, CancellationToken ct = default) => Task.CompletedTask;
}
