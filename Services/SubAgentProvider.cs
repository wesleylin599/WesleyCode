using System.ComponentModel;
using System.Security;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using WesleyCode.Extensions;

namespace WesleyCode.Services;

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
        "规划代理,用于设计实现任务清单,使用示例`use_subAgent(planner,<需求>)`",
        """
        你是一个规划代理.设计实现任务清单.不要做更改.
        每个任务需要按顺序且可独立执行.
        任务的总数不要超过 10 条.
        """,
        new ReasoningOptions { Output = ReasoningOutput.Summary }
    );
    private static readonly AgentContent Executor = new(
        "executor",
        "执行代理,用于执行专注任务,使用示例`use_subAgent(executor,<任务描述>)`",
        """
        你是一个执行代理.使用工具简洁高效地完成任务.
        执行完成后总结做了什么.
        """,
        new ReasoningOptions { Output = ReasoningOutput.Summary }
    );

    private readonly AITool[] _tools;
    private readonly IChatClient _client;
    private readonly AIContextProvider[] _providers;
    private readonly ILogger<SubAgentProvider> _logger;
    private readonly string _agentPrompt;
    private readonly Dictionary<string, AgentContent> _agents = new(StringComparer.OrdinalIgnoreCase)
    {
        [Planner.Name] = Planner,
        [Executor.Name] = Executor,
    };

    public SubAgentProvider(IChatClient client, AIContextProvider[]? providers = null, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _providers = providers ?? [];
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SubAgentProvider>();
        _tools = [AIFunctionFactory.Create(this.UseSubAgentAsync, name: "use_subAgent", description: "调用子代理,获取子代理执行结果.")];
        _agentPrompt = BuildAgentPrompt();
    }

    protected override ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Successfully loaded {AgentCount} subAgent", _agents.Count);
        var instructionPrompt = string.Format(DefaultInstructionPrompt, _agentPrompt);
        return ValueTask.FromResult(new AIContext { Instructions = instructionPrompt, Tools = _tools });
    }

    private record AgentContent(string Name, string Description, string Instructions, ReasoningOptions? Reasoning = null);

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

        _logger.LogInformation($"{content.Name} request...");

        var reasoning = content.Reasoning ?? new ReasoningOptions();
        AIAgent subAgent = _client.AsAIAgent(
            options: new ChatClientAgentOptions
            {
                Name = content.Name,
                Description = content.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = content.Instructions,
                    AllowMultipleToolCalls = true,
                    ToolMode = ChatToolMode.Auto,
                    Tools = [ToolManager.CommandFunction],
                    Reasoning = reasoning,
                },
                AIContextProviders = _providers,
            }
        );
        var session = await subAgent.CreateSessionAsync(cancellationToken);

        var response = await subAgent.ExecuteAsync(input, session, cancellationToken: cancellationToken);

        _logger.LogInformation($"{content.Name} response...");

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
