using System.Collections.Concurrent;

namespace Server.Services;

public sealed class SnapshotService
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<int, (float X, float Y)>> _rooms = new();

    private ConcurrentDictionary<int, (float X, float Y)> Room(string roomId) =>
        _rooms.GetOrAdd(roomId, _ => new ConcurrentDictionary<int, (float X, float Y)>());

    public ConcurrentDictionary<int, (float X, float Y)> RawRoom(string roomId) => Room(roomId);

    public void Set(string roomId, int playerId, float x, float y) => Room(roomId)[playerId] = (x, y);

    public void Remove(string roomId, int playerId)
    {
        if (_rooms.TryGetValue(roomId, out var dict))
            dict.TryRemove(playerId, out _);
    }

    public IReadOnlyList<(string Room, int Count)> RoomList()
    {
        var list = new List<(string, int)>(_rooms.Count);
        foreach (var kv in _rooms) list.Add((kv.Key, kv.Value.Count));
        list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
        return list;
    }

    public float[] SerializeFlat(string roomId)
    {
        if (!_rooms.TryGetValue(roomId, out var dict)) return Array.Empty<float>();
        var snapshot = dict.ToArray();
        var arr = new float[snapshot.Length * 3];
        for (int i = 0; i < snapshot.Length; i++)
        {
            arr[i * 3]     = snapshot[i].Key;
            arr[i * 3 + 1] = snapshot[i].Value.X;
            arr[i * 3 + 2] = snapshot[i].Value.Y;
        }
        return arr;
    }
}
