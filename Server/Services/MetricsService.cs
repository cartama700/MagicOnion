namespace Server.Services;

public sealed class MetricsService
{
    private long _packetsProcessed;
    private long _connectedPlayers;
    private long _aoiHitsAccum;
    private long _aoiSamples;

    public void IncrementPacket() => Interlocked.Increment(ref _packetsProcessed);
    public void PlayerJoined() => Interlocked.Increment(ref _connectedPlayers);
    public void PlayerLeft() => Interlocked.Decrement(ref _connectedPlayers);

    public void RecordAoiHits(int hits)
    {
        Interlocked.Add(ref _aoiHitsAccum, hits);
        Interlocked.Increment(ref _aoiSamples);
    }

    public long ConnectedPlayers => Interlocked.Read(ref _connectedPlayers);

    /// <summary>Monotonic packet counter. Never resets — readers compute deltas themselves.</summary>
    public long TotalPackets => Interlocked.Read(ref _packetsProcessed);

    /// <summary>Snapshot+reset for AOI hits — used by KpiRollupJob exclusively.</summary>
    public double GetAndResetAvgAoi()
    {
        var hits = Interlocked.Exchange(ref _aoiHitsAccum, 0);
        var samples = Interlocked.Exchange(ref _aoiSamples, 0);
        return samples == 0 ? 0 : (double)hits / samples;
    }
}
