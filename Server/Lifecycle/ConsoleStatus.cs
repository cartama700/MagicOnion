using System.Diagnostics;
using Server.Services;

namespace Server.Lifecycle;

/// <summary>
/// 포트폴리오용 콘솔 출력: 시작 배너 + 5초 주기 한 줄 상태.
/// 프레임워크 기본 로그(Hosting/Kestrel/Routing) 는 appsettings 에서 Warning 으로 낮춰 노이즈 제거.
/// </summary>
public sealed class ConsoleStatus : BackgroundService
{
    private const int TickSeconds = 5;
    private const int BoxWidth = 62;

    private readonly IConfiguration _cfg;
    private readonly MetricsService _metrics;
    private readonly KpiSnapshot _kpi;
    private readonly OptimizationMode _opt;
    private readonly IHostApplicationLifetime _lifetime;

    public ConsoleStatus(IConfiguration cfg, MetricsService metrics,
        KpiSnapshot kpi, OptimizationMode opt, IHostApplicationLifetime lifetime)
    {
        _cfg = cfg;
        _metrics = metrics;
        _kpi = kpi;
        _opt = opt;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await WaitForStartedAsync(stoppingToken);
        if (stoppingToken.IsCancellationRequested) return;

        PrintBanner();

        var prevPackets = _metrics.TotalPackets;
        var sw = Stopwatch.StartNew();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(TickSeconds), stoppingToken); }
            catch (OperationCanceledException) { break; }

            var now = _metrics.TotalPackets;
            var elapsedSec = sw.Elapsed.TotalSeconds;
            sw.Restart();
            var qps = elapsedSec > 0 ? (long)((now - prevPackets) / elapsedSec) : 0;
            prevPackets = now;
            PrintStatus(qps);
        }
    }

    private Task WaitForStartedAsync(CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var startedReg = _lifetime.ApplicationStarted.Register(() => tcs.TrySetResult());
        using var ctReg = ct.Register(() => tcs.TrySetCanceled(ct));
        return tcs.Task.ContinueWith(_ => { }, TaskScheduler.Default);
    }

    private void PrintBanner()
    {
        var mysql = !string.IsNullOrWhiteSpace(_cfg.GetConnectionString("Mysql"));
        var redis = !string.IsNullOrWhiteSpace(_cfg.GetConnectionString("Redis"));
        var backplane = !string.IsNullOrWhiteSpace(_cfg.GetConnectionString("RedisBackplane")) || redis;

        var lines = new[]
        {
            Top(),
            Row("MagicOnion Realtime Server  (PoC)", center: true),
            Mid(),
            Row($"HTTP API     : http://localhost:5050"),
            Row($"StreamingHub : http://localhost:5001  (HTTP/2)"),
            Row($"Dashboard    : http://localhost:5050/"),
            Row(""),
            Row($"MySQL        : {(mysql     ? "connected" : "disabled (in-memory)")}"),
            Row($"Redis        : {(redis     ? "connected" : "disabled (in-memory)")}"),
            Row($"Backplane    : {(backplane ? "enabled"   : "disabled")}"),
            Row($"Optimization : {(_opt.IsOn ? "ON"        : "OFF")}   (toggle: POST /api/optimize?on=)"),
            Bot(),
        };

        Console.WriteLine();
        foreach (var line in lines) Console.WriteLine(line);
        Console.WriteLine($"   ready · status updates every {TickSeconds}s · Ctrl+C to stop");
        Console.WriteLine();
    }

    private void PrintStatus(long qps)
    {
        var ts = DateTime.Now.ToString("HH:mm:ss");
        var players = _metrics.ConnectedPlayers;
        var p50 = _kpi.LastP50Ms;
        var p95 = _kpi.LastP95Ms;
        var p99 = _kpi.LastP99Ms;
        var aoi = _kpi.LastAvgAoi;
        var mode = _opt.IsOn ? "optimized" : "naive    ";
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);

        Console.WriteLine(
            $"[{ts}] players={players,5}  qps={qps,6}  p50={p50,4}ms  p95={p95,4}ms  p99={p99,4}ms  aoi={aoi,5:0.0}  gc={gc0,3}/{gc1,2}/{gc2}  mode={mode}");
    }

    private static string Top() => "╔" + new string('═', BoxWidth) + "╗";
    private static string Bot() => "╚" + new string('═', BoxWidth) + "╝";
    private static string Mid() => "╠" + new string('═', BoxWidth) + "╣";

    private static string Row(string content, bool center = false)
    {
        if (center)
        {
            var pad = Math.Max(0, BoxWidth - content.Length);
            var left = pad / 2;
            var right = pad - left;
            return "║" + new string(' ', left) + content + new string(' ', right) + "║";
        }
        var inner = " " + content;
        if (inner.Length > BoxWidth) inner = inner[..BoxWidth];
        return "║" + inner.PadRight(BoxWidth) + "║";
    }
}
