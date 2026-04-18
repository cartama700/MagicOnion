namespace Server.Services.Llm;

// Phase 16 — LLM Provider Registry.
// 본업(Personia) 의 멀티 프로바이더 라우팅 패턴을 게임 서버 관측성에 이식.
// OpenAI 장애/과금 시 Mock/로컬 모델로 failover 가능한 구조.
public interface ILlmProvider
{
    /// <summary>로깅/UI 배지용 식별자 ("mock" · "openai" 등).</summary>
    string Name { get; }

    /// <summary>
    /// 프롬프트를 스트리밍 토큰으로 반환. 한 yield = 한 chunk.
    /// Mock 은 결정적 캔드 응답, 실 프로바이더는 SSE 파싱.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken ct = default);
}
