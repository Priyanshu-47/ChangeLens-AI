namespace ChangeLens.Infrastructure.Options;

/// <summary>Configuration for the internal Python AI service client.</summary>
public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public const string ContractVersion = "1";

    /// <summary>Base URL of the AI service, e.g. http://localhost:8000 (compose: http://ai-service:8000).</summary>
    public string BaseUrl { get; set; } = "http://localhost:8000";

    /// <summary>Shared internal key sent as X-Internal-Key. Dev placeholder in Development; required elsewhere.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>HTTP timeout for one analysis call (the AI service enforces its own Gemini timeout inside this).</summary>
    public int TimeoutSeconds { get; set; } = 120;
}
