namespace TestConsole5.Options;

internal sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string Name { get; set; } = string.Empty;
    public string Instructions { get; set; } = string.Empty;
}
