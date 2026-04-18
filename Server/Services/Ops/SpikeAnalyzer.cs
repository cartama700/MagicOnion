using Server.Services.Llm;

namespace Server.Services.Ops;

// P99 Latency / Alloc Rate / Gen0 스파이크 원인 추정기.
// 현재 KpiSnapshot + MetricsService + OptimizationMode 를 구조화된 프롬프트로 직렬화해 LLM 에 주입.
// 프롬프트 직렬화 로직만 분리해둬 실 호출 없이 단위 테스트 가능.
public sealed class SpikeAnalyzer
{
    private readonly ILlmProvider _llm;
    private readonly KpiSnapshot _kpi;
    private readonly MetricsService _metrics;
    private readonly OptimizationMode _opt;

    public SpikeAnalyzer(ILlmProvider llm, KpiSnapshot kpi, MetricsService metrics, OptimizationMode opt)
    {
        _llm = llm;
        _kpi = kpi;
        _metrics = metrics;
        _opt = opt;
    }

    public IAsyncEnumerable<string> AnalyzeAsync(int minutes, CancellationToken ct = default)
    {
        var (system, user) = BuildPrompts(minutes);
        return _llm.StreamAsync(system, user, ct);
    }

    // 프롬프트 직렬화를 분리해 단위 테스트 용이성 확보.
    public (string System, string User) BuildPrompts(int minutes)
    {
        var system =
            "당신은 실시간 게임 서버의 온콜 엔지니어 보조입니다. " +
            "제공되는 텔레메트리를 읽고 P99 Latency 상승의 원인을 추정하고 " +
            "즉각적 완화 조치를 한국어로 간결하게 제시하세요. " +
            "이 서버에 실제 존재하는 조치(POST /api/optimize, Redis Backplane, HPA 스케일 아웃, " +
            "Graceful drain, Write-Behind 큐 모니터링)만 제안하세요.";

        var user =
            $"## 관측 구간: 최근 {minutes}분\n" +
            $"## 현재 상태\n" +
            $"- 접속자(ConnectedPlayers): {_metrics.ConnectedPlayers}\n" +
            $"- TPS(packets/sec): last={_kpi.LastPacketsPerSec}, peak={_kpi.PeakPacketsPerSec}, avg={_kpi.AvgPacketsPerSec:F1}\n" +
            $"- Avg AOI hits: {_kpi.LastAvgAoi:F2}\n" +
            $"- Latency: P50={_kpi.LastP50Ms}ms, P95={_kpi.LastP95Ms}ms, P99={_kpi.LastP99Ms}ms, Avg={_kpi.LastAvgLatencyMs:F2}ms\n" +
            $"- GC: AllocRate={_kpi.LastAllocRateMb:F2} MB/s, Gen0/s={_kpi.LastGen0PerSec}, Gen1/s={_kpi.LastGen1PerSec}\n" +
            $"- Zero-Alloc 토글: {(_opt.IsOn ? "ON" : "OFF")}\n" +
            $"- 누적: totalPackets={_kpi.TotalPackets}, samples={_kpi.TotalSamples}\n" +
            $"- 마지막 업데이트(UTC): {_kpi.LastUpdatedUtc:O}\n\n" +
            $"## 질문\n" +
            $"위 지표에서 P99 가 평상(~2ms) 대비 높다면 가장 유력한 원인과 " +
            $"즉각 조치를 3~5개 bullet 로 제시하세요. " +
            $"가능하면 '토글 · 스케일아웃 · 부하 재분포 · 큐 드레인' 중 하나로 수렴시키세요.";

        return (system, user);
    }
}
