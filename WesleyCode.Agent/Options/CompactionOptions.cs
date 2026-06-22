namespace WesleyCode.Agent.Options;

internal sealed class CompactionOptions
{
    public int ToolResultMessageLimit { get; set; } = 1500;
    public int SummaryTokenLimit { get; set; } = 10000;
    public int SlidingWindowTurnLimit { get; set; } = 10;
    public int TruncationGroupsLimit { get; set; } = 30000;
}
