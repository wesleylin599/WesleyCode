namespace WesleyCode.Agent.Options;

public sealed class CompactionOptions
{
    public int ToolResultMessageLimit { get; set; } = 1500;
    public int SlidingWindowTurnLimit { get; set; } = 10;
    public int TruncationGroupsLimit { get; set; } = 30000;
}
