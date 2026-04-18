namespace Server.Services.Llm;

public sealed class LlmOptions
{
    /// <summary>"mock" (기본) / "openai". 기본이 mock 이라 API 키 없어도 전 경로 동작.</summary>
    public string Provider { get; set; } = "mock";
    public string? ApiKey { get; set; }
    public string Model { get; set; } = "gpt-4o-mini";
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public int MaxTokens { get; set; } = 512;
    public double Temperature { get; set; } = 0.2;
}
