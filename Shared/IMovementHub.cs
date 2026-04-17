using MagicOnion;

namespace Shared;

public interface IMovementHub : IStreamingHub<IMovementHub, IMovementHubReceiver>
{
    ValueTask JoinAsync(int playerId, string roomId, float startX, float startY);
    ValueTask MoveAsync(PlayerMoveDto moveData);
    ValueTask LeaveAsync();
}

public interface IMovementHubReceiver
{
    void OnPlayerMoved(PlayerMoveDto moveData);
    void OnPlayerJoined(PlayerMoveDto moveData);
    void OnPlayerLeft(int playerId);
}
