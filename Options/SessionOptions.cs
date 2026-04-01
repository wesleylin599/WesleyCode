namespace TestConsole5.Options;

internal sealed class SessionOptions
{
    public string DirectoryName { get; set; } = "session";
    public int SaveDebounceSeconds { get; set; } = 2;
}
