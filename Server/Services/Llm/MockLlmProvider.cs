using System.Runtime.CompilerServices;

namespace Server.Services.Llm;

// 결정적 모의 LLM. 네트워크 없이 스트리밍 UX 그대로 재현.
// 면접 데모 시 API 키/과금/장애 걱정 없이 Phase 16 전체 플로우 시연 가능.
public sealed class MockLlmProvider : ILlmProvider
{
    public string Name => "mock";

    public async IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = Compose(userPrompt);
        foreach (var chunk in Tokenize(response))
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(25, ct); // 토큰 간 지연 — 실 LLM 체감 재현
            yield return chunk;
        }
    }

    private static string Compose(string userPrompt)
    {
        if (userPrompt.Contains("P99", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("spike", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("스파이크"))
        {
            return
                "[Mock Provider · P99 스파이크 분석]\n" +
                "최근 관측 구간에서 P99 Latency 상승 신호를 확인했습니다.\n" +
                "- 가설 1: Zero-Allocation 모드가 OFF 인 상태에서 cluster 부하가 유입되면 " +
                "AOI 필터의 LINQ 경로가 호출당 ~82KB 를 할당합니다. " +
                "Gen0 GC 가 30~60Hz 로 뛰면서 P99 가 2ms → 수십ms 로 튈 수 있습니다.\n" +
                "- 가설 2: Redis Backplane 의 Pub/Sub 지연이 가세한 경우. " +
                "단일 프로세스 모드로 전환해 원인을 격리할 수 있습니다.\n" +
                "- 조치 A: POST /api/optimize?on=true — 5초 내 P99 재측정.\n" +
                "- 조치 B: docker compose --profile scale 에서 Redis Backplane 비활성화로 A/B.\n" +
                "- 조치 C: 지속되면 HPA 로 레플리카 증설 후 Graceful drain 동반.\n" +
                "[분석 완료]";
        }
        if (userPrompt.Contains("abuse", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("cheat", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("어뷰") || userPrompt.Contains("치팅"))
        {
            return
                "[Mock Provider · 의심 패턴 후보]\n" +
                "1. player 127 — score/sec 비율이 룸 평균의 18배. 매크로/자동조준 가능성.\n" +
                "2. player 341 — 3초 간격 재접속 × 12회. 연결 기반 어뷰징 패턴.\n" +
                "3. player 77 — Leave 직후 동일 점수로 재진입. rejoin 로직 오용 의심.\n" +
                "(실제 판정은 추가 룰/지표가 필요. 본 응답은 패턴 탐지 시드용)";
        }
        if (userPrompt.Contains("drain", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("shutdown", StringComparison.OrdinalIgnoreCase) ||
            userPrompt.Contains("드레인"))
        {
            return
                "[Mock Provider · Drain 요약]\n" +
                "Graceful drain 진행 중. 잔여 세션 3, MatchWriteQueue 대기 12건. " +
                "현재 속도 유지 시 약 5초 내 완료 예상. 정상 범위 — 개입 불필요.";
        }
        return
            "[Mock Provider] 적합한 프롬프트 템플릿을 찾지 못했습니다. " +
            "Llm:Provider 를 openai 로 전환하면 실 분석이 가능합니다.";
    }

    // 단어/구두점 단위로 쪼개 스트리밍처럼 보이게.
    private static IEnumerable<string> Tokenize(string s)
    {
        int start = 0;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == ' ' || c == '\n' || c == '.' || c == ',' || c == ':')
            {
                if (i + 1 > start) yield return s.Substring(start, i + 1 - start);
                start = i + 1;
            }
        }
        if (start < s.Length) yield return s.Substring(start);
    }
}
