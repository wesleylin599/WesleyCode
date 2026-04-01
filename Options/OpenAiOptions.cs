namespace TestConsole5.Options;

internal sealed class OpenAiOptions
{
    public const string SectionName = "OpenAI";

    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
}
