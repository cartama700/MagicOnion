using Server.Services;

namespace Server.Jobs;

public sealed class KpiRollupJob : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(1);

    private readonly MetricsService _metrics;
    private readonly KpiSnapshot _kpi;
    private readonly LatencyHistogram _latency;
    private readonly ILogger<KpiRollupJob> _logger;
    private long _lastPacketsCheckpoint;
    private long _lastAllocBytes;
    private int _lastGen0;
    private int _lastGen1;

    public KpiRollupJob(MetricsService metrics, KpiSnapshot kpi, LatencyHistogram latency, ILogger<KpiRollupJob> logger)
    {
        _metrics = metrics;
        _kpi = kpi;
        _latency = latency;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KpiRollupJob started — period {PeriodSec}s", Period.TotalSeconds);
        using var timer = new PeriodicTimer(Period);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            var players = _metrics.ConnectedPlayers;
            var packetsTotal = _metrics.TotalPackets;
            var deltaPackets = packetsTotal - _lastPacketsCheckpoint;
            _lastPacketsCheckpoint = packetsTotal;
            var avgAoi = _metrics.GetAndResetAvgAoi();
            var (p50, p95, p99, avgLat, _) = _latency.SnapshotAndReset();
            _kpi.Record(players, Math.Max(0, deltaPackets), avgAoi, p50, p95, p99, avgLat);

            // GC rate (per-second deltas) — 토글 즉시 차이가 보이도록 누적이 아닌 속도로.
            var allocNow = GC.GetTotalAllocatedBytes();
            var gen0Now = GC.CollectionCount(0);
            var gen1Now = GC.CollectionCount(1);
            if (_lastAllocBytes > 0)
            {
                var allocRateMb = Math.Max(0, allocNow - _lastAllocBytes) / 1_048_576.0 / Period.TotalSeconds;
                var gen0PerSec = (long)(Math.Max(0, gen0Now - _lastGen0) / Period.TotalSeconds);
                var gen1PerSec = (long)(Math.Max(0, gen1Now - _lastGen1) / Period.TotalSeconds);
                _kpi.RecordGc(allocRateMb, gen0PerSec, gen1PerSec);
            }
            _lastAllocBytes = allocNow;
            _lastGen0 = gen0Now;
            _lastGen1 = gen1Now;
        }
    }
}
