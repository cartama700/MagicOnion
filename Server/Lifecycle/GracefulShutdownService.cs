using Server.Services;

namespace Server.Lifecycle;

/// <summary>
/// K8s rolling update / HPA scale-down 시 유저가 강제로 튕기지 않도록 드레인.
///
/// 순서:
///   1. SIGTERM → StoppingAsync 진입
///   2. ReadinessGate.MarkNotReady() → /health/ready 가 503 → Service 에서 엔드포인트 제거
///   3. 활성 세션이 0 이 되거나 DrainTimeout 경과까지 대기
///   4. StoppedAsync 로 넘어가면 호스트가 hosted services 를 Stop → MatchFlushJob 이 남은 배치 drain
/// </summary>
public sealed class GracefulShutdownService : IHostedLifecycleService
{
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly ReadinessGate _gate;
    private readonly MetricsService _metrics;
    private readonly ILogger<GracefulShutdownService> _logger;

    public GracefulShutdownService(ReadinessGate gate, MetricsService metrics, ILogger<GracefulShutdownService> logger)
    {
        _gate = gate;
        _metrics = metrics;
        _logger = logger;
    }

    public Task StartingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartedAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StartAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task StoppingAsync(CancellationToken cancellationToken)
    {
        _gate.MarkNotReady();
        var players = _metrics.ConnectedPlayers;
        _logger.LogWarning("Graceful shutdown started — {Players} active sessions, draining…", players);

        using var timeoutCts = new CancellationTokenSource(DrainTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            while (_metrics.ConnectedPlayers > 0 && !linked.IsCancellationRequested)
                await Task.Delay(PollInterval, linked.Token);
        }
        catch (OperationCanceledException) { }

        var remaining = _metrics.ConnectedPlayers;
        if (remaining == 0) _logger.LogInformation("Graceful drain complete — all sessions closed");
        else                _logger.LogWarning("Drain timeout hit with {Remaining} sessions still active", remaining);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    public Task StoppedAsync(CancellationToken ct) => Task.CompletedTask;
}
