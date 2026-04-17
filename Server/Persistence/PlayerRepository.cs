using Dapper;
using MySqlConnector;

namespace Server.Persistence;

public sealed class PlayerRepository : IPlayerRepository
{
    private readonly string _connectionString;

    public PlayerRepository(string connectionString) => _connectionString = connectionString;

    public async Task UpsertProfileAsync(int playerId, string displayName, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO player_profile (player_id, display_name)
            VALUES (@playerId, @displayName)
            ON DUPLICATE KEY UPDATE display_name = VALUES(display_name), last_seen = CURRENT_TIMESTAMP
            """;
        await using var conn = new MySqlConnection(_connectionString);
        await conn.ExecuteAsync(new CommandDefinition(sql, new { playerId, displayName }, cancellationToken: ct));
    }

    public async Task BulkInsertMatchesAsync(IReadOnlyList<MatchRecord> records, CancellationToken ct = default)
    {
        if (records.Count == 0) return;

        const string sql = """
            INSERT INTO match_record (id, player_id, room_id, score, joined_at, left_at)
            VALUES (@Id, @PlayerId, @RoomId, @Score, @JoinedAtUtc, @LeftAtUtc)
            """;

        await using var conn = new MySqlConnection(_connectionString);
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // Dapper binds Guid → BINARY(16) automatically for MySqlConnector when the column is BINARY(16).
        var rows = records.Select(r => new
        {
            Id = r.Id.ToByteArray(),
            r.PlayerId, r.RoomId, r.Score,
            r.JoinedAtUtc, r.LeftAtUtc
        });
        await conn.ExecuteAsync(new CommandDefinition(sql, rows, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }
}
