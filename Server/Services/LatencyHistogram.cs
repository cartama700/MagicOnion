namespace Server.Services;

/// <summary>
/// Lock-free bucketed histogram for millisecond-scale latency.
/// Log-ish buckets covering 1ms–60s. Zero-allocation on the hot path (just Interlocked.Increment).
/// </summary>
public sealed class LatencyHistogram
{
    // Upper bound (ms, inclusive) for each bucket. Last bucket is overflow.
    private static readonly int[] Bounds =
    {
        1, 2, 5, 10, 20, 35, 50, 75, 100, 150, 200, 300, 500, 750, 1000, 2000, 5000, 10000, int.MaxValue
    };
    private readonly long[] _counts = new long[Bounds.Length];
    private long _total;
    private long _sum;

    public int BucketCount => Bounds.Length;

    public void Record(int latencyMs)
    {
        if (latencyMs < 0) latencyMs = 0;
        int idx = 0;
        while (idx < Bounds.Length - 1 && latencyMs > Bounds[idx]) idx++;
        Interlocked.Increment(ref _counts[idx]);
        Interlocked.Increment(ref _total);
        Interlocked.Add(ref _sum, latencyMs);
    }

    /// <summary>Snapshot + reset. Returns p50/p95/p99 (ms) and avg.</summary>
    public (int P50, int P95, int P99, double Avg, long Samples) SnapshotAndReset()
    {
        var counts = new long[_counts.Length];
        long total = 0, sum = 0;
        for (int i = 0; i < _counts.Length; i++)
        {
            counts[i] = Interlocked.Exchange(ref _counts[i], 0);
            total += counts[i];
        }
        sum = Interlocked.Exchange(ref _sum, 0);
        Interlocked.Exchange(ref _total, 0);

        if (total == 0) return (0, 0, 0, 0, 0);

        int p50 = Percentile(counts, total, 0.50);
        int p95 = Percentile(counts, total, 0.95);
        int p99 = Percentile(counts, total, 0.99);
        return (p50, p95, p99, (double)sum / total, total);
    }

    private static int Percentile(long[] counts, long total, double q)
    {
        long target = (long)Math.Ceiling(total * q);
        long acc = 0;
        for (int i = 0; i < counts.Length; i++)
        {
            acc += counts[i];
            if (acc >= target) return Bounds[i] == int.MaxValue ? Bounds[i - 1] * 2 : Bounds[i];
        }
        return Bounds[^1];
    }
}
