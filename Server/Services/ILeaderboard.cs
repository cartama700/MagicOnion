namespace Server.Services;

public interface ILeaderboard
{
    Task AddScoreAsync(string roomId, int playerId, double delta, CancellationToken ct = default);
    Task<IReadOnlyList<(int PlayerId, double Score)>> TopAsync(string roomId, int n, CancellationToken ct = default);
    Task RemoveAsync(string roomId, int playerId, CancellationToken ct = default);
}
