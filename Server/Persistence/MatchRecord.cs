namespace Server.Persistence;

public sealed record MatchRecord(
    Guid Id,
    int PlayerId,
    string RoomId,
    DateTime JoinedAtUtc,
    DateTime LeftAtUtc,
    long Score);
