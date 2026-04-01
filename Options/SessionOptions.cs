namespace TestConsole5.Options;

internal sealed class SessionOptions
{
    public const string SectionName = "Session";

    public string DirectoryName { get; set; } = "session";
    public string SessionDir { get; set; } = string.Empty;
    public int SaveDebounceSeconds { get; set; } = 2;
}
