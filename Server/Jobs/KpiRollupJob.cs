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
        }
    }
}
