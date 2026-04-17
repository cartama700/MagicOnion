using System.Buffers;
using System.Collections.Concurrent;
using BenchmarkDotNet.Attributes;

namespace Benchmarks;

[MemoryDiagnoser]
[GcServer(true)]
public class AoiBenchmarks
{
    private const float AoiRadius = 200f;
    private const float AoiRadiusSq = AoiRadius * AoiRadius;
    private const float WorldW = 1200f;
    private const float WorldH = 720f;

    [Params(100, 1_000, 5_000)]
    public int PlayerCount;

    private ConcurrentDictionary<int, (float X, float Y)> _world = null!;
    private (int Id, float X, float Y) _move;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _world = new ConcurrentDictionary<int, (float X, float Y)>();
        for (int i = 1; i <= PlayerCount; i++)
            _world[i] = (rng.NextSingle() * WorldW, rng.NextSingle() * WorldH);

        _move = (1, _world[1].X, _world[1].Y);
    }

    [Benchmark(Baseline = true)]
    public int Naive_LinqFilter()
    {
        var copy = _world.ToArray();
        var hits = copy
            .Where(kv => kv.Key != _move.Id)
            .Where(kv =>
            {
                var dx = kv.Value.X - _move.X;
                var dy = kv.Value.Y - _move.Y;
                return dx * dx + dy * dy <= AoiRadiusSq;
            })
            .Select(kv => kv.Key)
            .ToList();
        return hits.Count;
    }

    [Benchmark]
    public int Optimized_PoolBuffer()
    {
        var pool = ArrayPool<int>.Shared;
        var buf = pool.Rent(256);
        try
        {
            int count = 0;
            float x = _move.X, y = _move.Y, sq = AoiRadiusSq;
            int self = _move.Id;
            foreach (var kv in _world)
            {
                if (kv.Key == self) continue;
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
