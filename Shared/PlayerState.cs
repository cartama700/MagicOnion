using MessagePack;

namespace Shared;

[MessagePackObject]
public struct PlayerMoveDto
{
    [Key(0)] public int PlayerId;
    [Key(1)] public float X;
    [Key(2)] public float Y;
    /// <summary>Unix epoch milliseconds when this packet was produced by the client.
    /// Server uses it to compute end-to-end latency (Phase 15).</summary>
    [Key(3)] public long SentAtMs;
}
