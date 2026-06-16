using System.ComponentModel;
using System.Security;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WesleyCode.Agent.Extensions;

namespace WesleyCode.Agent.Services;

internal sealed class SubAgentProvider : AIContextProvider
{
    private const string DefaultInstructionPrompt = """
        你拥有以下的智能体,在需要的时候使用智能体去完成任务:

        <available_agents>
        {0}
        </available_agents>

        使用`use_subAgent`调用子代理.
        多步骤任务优先使用子代理.
        子代理拥有独立的上下文.
        """;

    private static readonly AgentContent Planner = new(
        "planner",
        "规划代理,用于设计任务清单",
        """
        对需求进行分析,设计实现任务清单,不要做更改.
        任务清单应该是实现需求的必要步骤,每个任务都应该是独立的,并且可以被单独执行的.
        任务的总数不要超过 10 条.
        """,
        [ToolManager.CommandFunction],
        new ReasoningOptions { Output = ReasoningOutput.Summary },
        ChatResponseFormat.ForJsonSchema<List<TaskItem>>()
    );
    private static readonly AgentContent Executor = new(
        "executor",
        "执行代理,用于完成独立任务",
        """
        使用工具简洁高效地完成任务
        仔细阅读任务描述,如果不清楚任务需求,可以使用工具进行查询,直到完全理解任务需求.
        执行完成后输出操作总结与执行结论.
        """,
        [ToolManager.CommandFunction],
        new ReasoningOptions { Output = ReasoningOutput.Summary },
        ChatResponseFormat.Text
    );

    private readonly AITool[] _tools;
    private readonly IChatClient _client;
    private readonly IOutputCapture _capture;
    private readonly AIContextProvider[] _providers;
    private readonly ILogger<SubAgentProvider> _logger;
    private readonly string _agentPrompt;

    private readonly Dictionary<string, AgentContent> _agents = new(StringComparer.OrdinalIgnoreCase)
    {
        [Planner.Name] = Planner,
        [Executor.Name] = Executor,
    };

    public SubAgentProvider(IChatClient client, IOutputCapture capture, AIContextProvider[]? providers = null, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _capture = capture;
        _providers = providers ?? [];
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SubAgentProvider>();
        _tools = [AIFunctionFactory.Create(this.UseSubAgentAsync, name: "use_subAgent", description: "调用子代理,获取子代理执行结果.")];
        _agentPrompt = BuildAgentPrompt();
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Successfully loaded {AgentCount} subAgent", _agents.Count);
        var instructionPrompt = string.Format(DefaultInstructionPrompt, _agentPrompt);
        return ValueTask.FromResult(new AIContext { Instructions = instructionPrompt, Tools = _tools });
    }

    private record AgentContent(
        string Name,
        string Description,
        string Instructions,
        AITool[]? Tools = null,
        ReasoningOptions? Reasoning = null,
        ChatResponseFormat? ResponseFormat = null
    );

    private async Task<string> UseSubAgentAsync(
        [Description("子代理名称")] string name,
        [Description("子代理输入")] string input,
        CancellationToken cancellationToken = default
    )
    {
        if (!_agents.TryGetValue(name, out var content))
        {
            return "Error: 未找到该子代理.";
        }

        var tools = content.Tools ?? [];
        var reasoning = content.Reasoning ?? new ReasoningOptions();
        var responseFormat = content.ResponseFormat ?? ChatResponseFormat.Text;

        AIAgent subAgent = _client.AsAIAgent(
            options: new ChatClientAgentOptions
            {
                Name = content.Name,
                Description = content.Description,
                ChatOptions = new ChatOptions
                {
                    ToolMode = ChatToolMode.Auto,
                    Instructions = content.Instructions,
                    ResponseFormat = responseFormat,
                    Reasoning = reasoning,
                    Tools = tools,
                },
                AIContextProviders = _providers,
            }
        );
        var session = await subAgent.CreateSessionAsync(cancellationToken);

        var response = await subAgent.ExecuteAsync(input, session, _capture, cancellationToken);

        _logger.LogDebug($"{content.Name} response...");

        return string.IsNullOrWhiteSpace(response.Text) ? "Error:未获取到输出结果" : response.Text;
    }

    private string BuildAgentPrompt()
    {
        var sb = new StringBuilder();
        foreach (var agentContent in _agents.Values)
        {
            sb.AppendLine("  <agent>");
            sb.AppendLine($"    <name>{SecurityElement.Escape(agentContent.Name)}</name>");
            sb.AppendLine($"    <description>{SecurityElement.Escape(agentContent.Description)}</description>");
            sb.AppendLine("  </agent>");
            _logger.LogInformation("Loaded subAgent: {SubAgentName}", agentContent.Name);
        }
        return sb.ToString().TrimEnd();
    }
}
