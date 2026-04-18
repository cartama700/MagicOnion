using Server.Services.Llm;
using Server.Services.Ops;

namespace Server.Endpoints;

// Phase 16 — AI 운영 보조자 엔드포인트.
// 응답은 Server-Sent Events (text/event-stream) 로 스트리밍. JS EventSource 로 수신.
// EventSource 는 GET 만 지원하므로 query param 으로 인자 전달.
public static class OpsEndpoints
{
    public static void MapOps(this IEndpointRouteBuilder app)
    {
        // 현재 프로바이더 식별 — 대시보드 배지/안전장치 표시용.
        app.MapGet("/api/ops/provider", (ILlmProvider llm) =>
            Results.Json(new { provider = llm.Name }));

        // P99 스파이크 분석 — SSE 스트리밍
        app.MapGet("/api/ops/analyze/spike", async (
            HttpResponse resp,
            SpikeAnalyzer analyzer,
            int? minutes,
            CancellationToken ct) =>
        {
            resp.ContentType = "text/event-stream";
            resp.Headers["Cache-Control"] = "no-cache";
            resp.Headers["X-Accel-Buffering"] = "no"; // nginx 프록시 시 버퍼링 해제

            try
            {
                await foreach (var token in analyzer.AnalyzeAsync(minutes ?? 5, ct))
                {
                    // SSE 는 \n 을 메시지 구분자로 써서 그대로 실으면 프레이밍 꼬임.
                    var safe = token.Replace("\r", "").Replace("\n", "\\n");
                    await resp.WriteAsync($"data: {safe}\n\n", ct);
                    await resp.Body.FlushAsync(ct);
                }
                await resp.WriteAsync("event: done\ndata: end\n\n", ct);
                await resp.Body.FlushAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // 클라이언트 disconnect — 정상.
            }
            catch (Exception ex)
            {
                await resp.WriteAsync($"event: error\ndata: {ex.Message.Replace("\n", " ")}\n\n", CancellationToken.None);
            }
        });
    }
}
