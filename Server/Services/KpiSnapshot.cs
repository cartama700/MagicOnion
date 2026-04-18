namespace Server.Services;

public sealed class KpiSnapshot
{
    private long _peakPlayers;
    private long _peakPacketsPerSec;
    private long _totalSamples;
    private long _packetsAccum;
    private long _lastPacketsPerSec;
    private double _lastAvgAoi;
    private int _lastP50Ms;
    private int _lastP95Ms;
    private int _lastP99Ms;
    private double _lastAvgLatencyMs;
    private double _lastAllocRateMb;     // MB/sec 할당 속도 (직전 1초 구간)
    private long _lastGen0PerSec;
    private long _lastGen1PerSec;

    public DateTime LastUpdatedUtc { get; private set; } = DateTime.UtcNow;

    public long PeakPlayers => Interlocked.Read(ref _peakPlayers);
    public long PeakPacketsPerSec => Interlocked.Read(ref _peakPacketsPerSec);
    public long TotalSamples => Interlocked.Read(ref _totalSamples);
    public long TotalPackets => Interlocked.Read(ref _packetsAccum);
    public long LastPacketsPerSec => Interlocked.Read(ref _lastPacketsPerSec);
    public double LastAvgAoi => Volatile.Read(ref _lastAvgAoi);
    public int LastP50Ms => Volatile.Read(ref _lastP50Ms);
    public int LastP95Ms => Volatile.Read(ref _lastP95Ms);
    public int LastP99Ms => Volatile.Read(ref _lastP99Ms);
    public double LastAvgLatencyMs => Volatile.Read(ref _lastAvgLatencyMs);
    public double LastAllocRateMb => Volatile.Read(ref _lastAllocRateMb);
    public long LastGen0PerSec => Interlocked.Read(ref _lastGen0PerSec);
    public long LastGen1PerSec => Interlocked.Read(ref _lastGen1PerSec);

    public double AvgPacketsPerSec
    {
        get
        {
            var s = TotalSamples;
            return s == 0 ? 0 : (double)TotalPackets / s;
        }
    }

    public void RecordGc(double allocRateMb, long gen0PerSec, long gen1PerSec)
    {
        Volatile.Write(ref _lastAllocRateMb, allocRateMb);
        Interlocked.Exchange(ref _lastGen0PerSec, gen0PerSec);
        Interlocked.Exchange(ref _lastGen1PerSec, gen1PerSec);
    }

    public void Record(long players, long packetsLastSecond, double avgAoi,
                       int p50Ms = 0, int p95Ms = 0, int p99Ms = 0, double avgLatencyMs = 0)
    {
        UpdateMax(ref _peakPlayers, players);
        UpdateMax(ref _peakPacketsPerSec, packetsLastSecond);
        Interlocked.Exchange(ref _lastPacketsPerSec, packetsLastSecond);
        Volatile.Write(ref _lastAvgAoi, avgAoi);
        Volatile.Write(ref _lastP50Ms, p50Ms);
        Volatile.Write(ref _lastP95Ms, p95Ms);
        Volatile.Write(ref _lastP99Ms, p99Ms);
        Volatile.Write(ref _lastAvgLatencyMs, avgLatencyMs);
        Interlocked.Add(ref _packetsAccum, packetsLastSecond);
        Interlocked.Increment(ref _totalSamples);
        LastUpdatedUtc = DateTime.UtcNow;
    }

    private static void UpdateMax(ref long target, long candidate)
    {
        long cur;
        do { cur = Interlocked.Read(ref target); if (candidate <= cur) return; }
        while (Interlocked.CompareExchange(ref target, candidate, cur) != cur);
    }
}
