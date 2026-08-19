using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Options;

public sealed class ChatClientOptions
{
    public string? Provider { get; set; }
    public string? BaseUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ModelId { get; set; }
    public bool? AllowBackgroundResponses { get; set; }
    public bool? AllowMultipleToolCalls { get; set; }
    public ReasoningEffort? Effort { get; set; }
    public ReasoningOutput? Output { get; set; }
    public int? MaxOutputTokens { get; set; }
    public string StopMark { get; set; } = "[EOF]";
}
