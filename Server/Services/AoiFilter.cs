using System.Buffers;
using System.Collections.Concurrent;

namespace Server.Services;

public static class AoiFilter
{
    public const float DefaultRadius = 200f;

    public static int Naive(
        ConcurrentDictionary<int, (float X, float Y)> world,
        int selfId, float x, float y, float radius)
    {
        var sq = radius * radius;
        var copy = world.ToArray();
        return copy
            .Where(kv => kv.Key != selfId)
            .Where(kv =>
            {
                var dx = kv.Value.X - x;
                var dy = kv.Value.Y - y;
                return dx * dx + dy * dy <= sq;
            })
            .Select(kv => kv.Key)
            .ToList()
            .Count;
    }

    public static int Optimized(
        ConcurrentDictionary<int, (float X, float Y)> world,
        int selfId, float x, float y, float radius)
    {
        var sq = radius * radius;
        var pool = ArrayPool<int>.Shared;
        var buf = pool.Rent(256);
        try
        {
            int count = 0;
            foreach (var kv in world)
            {
                if (kv.Key == selfId) continue;
                var dx = kv.Value.X - x;
                var dy = kv.Value.Y - y;
                if (dx * dx + dy * dy > sq) continue;
                if (count >= buf.Length) break;
                buf[count++] = kv.Key;
            }
            return count;
        }
        finally
        {
            pool.Return(buf);
        }
    }
}
