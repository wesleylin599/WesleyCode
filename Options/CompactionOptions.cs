namespace WesleyCode.Options;

internal sealed class CompactionOptions
{
    public int MessageCountingLimit { get; set; } = 10;
    public int ToolResultTokenLimit { get; set; } = 1500;
    public int SummaryTokenLimit { get; set; } = 10000;
    public int SlidingWindowTurnLimit { get; set; } = 10;
    public int TruncationTokenLimit { get; set; } = 30000;
}
