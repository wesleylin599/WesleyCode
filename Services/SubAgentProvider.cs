using System.ComponentModel;
using System.Security;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestConsole5.Services;

internal class SubAgentProvider : AIContextProvider
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

    private static readonly AgentContent planner = new AgentContent(
        "planner",
        "规划代理,用于设计实现任务清单,使用示例`use_subAgent(planner,<需求>)`",
        """
        你是一个规划代理.设计实现任务清单.不要做更改.
        要求:
        将任务添加到任务队列中.
        每个任务需要可独立执行.
        任务总数不要超过 10 条.
        完成后更新任务清单.
        只输出`已更新任务清单`.
        """,
        [.. ToolManager.ReadFunctions, ToolManager.UpdateTasksFunction],
        new ReasoningOptions { Effort = ReasoningEffort.Low, Output = ReasoningOutput.Summary }
    );
    private static readonly AgentContent executor = new AgentContent(
        "executor",
        "执行代理,用于执行任务,每次只执行一条任务,使用示例`use_subAgent(executor,<任务标题>)`",
        """
        你是一个执行代理.高效简洁地完成任务.
        要求:
        从任务队列中获取任务标题对应任务并完成它.
        每次只执行一条任务,完成后更新任务清单.
        只输出`已更新任务清单`.
        """,
        ToolManager.AllFunctions,
        new ReasoningOptions { Effort = ReasoningEffort.High, Output = ReasoningOutput.Summary }
    );
    private static readonly AgentContent reviewer = new AgentContent(
        "reviewer",
        "审阅代理,用于对任务完成度进行评估,使用示例`use_subAgent(reviewer,<根据任务清单进行评估>)`",
        """
        你是一个审阅代理,评估任务完成度,不要做更改.
        要求:
        根据需求和执行结果完成评估.
        输出评估结论.
        """,
        ToolManager.ReadFunctions,
        new ReasoningOptions { Effort = ReasoningEffort.Medium, Output = ReasoningOutput.Full }
    );

    private readonly Dictionary<string, AgentContent> _agents = new(StringComparer.OrdinalIgnoreCase)
    {
        [planner.Name] = planner,
        [executor.Name] = executor,
        [reviewer.Name] = reviewer,
    };

    private readonly AITool[] _tools;
    private readonly IChatClient _client;
    private readonly AIContextProvider[] _providers;
    private readonly ILogger<SubAgentProvider> _logger;

    public SubAgentProvider(IChatClient client, AIContextProvider[]? providers = null, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _providers = providers ?? [];
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SubAgentProvider>();
        _tools = [AIFunctionFactory.Create(this.UseSubAgentAsync, name: "use_subAgent", description: "调用子代理,获取子代理执行结果.")];
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        foreach (var agentContent in _agents.Values)
        {
            sb.AppendLine("  <agent>");
            sb.AppendLine($"    <name>{SecurityElement.Escape(agentContent.Name)}</name>");
            sb.AppendLine($"    <description>{SecurityElement.Escape(agentContent.Description)}</description>");
            sb.AppendLine($"    <tools>");
            if (agentContent.Tools is not null)
            {
                foreach (var tool in agentContent.Tools)
                {
                    sb.AppendLine($"        <tool>");
                    sb.AppendLine($"            <name>{SecurityElement.Escape(tool.Name)}</name>");
                    sb.AppendLine($"            <description>{SecurityElement.Escape(tool.Description)}</description>");
                    sb.AppendLine($"        </tool>");
                }
            }
            sb.AppendLine($"    </tools>");
            sb.AppendLine("  </agent>");
            _logger.LogInformation($"Loaded subAgent: {agentContent.Name}");
        }
        _logger.LogInformation($"Successfully loaded {_agents.Count} subAgent");
        var instructionPrompt = string.Format(DefaultInstructionPrompt, sb.ToString().TrimEnd());
        return new AIContext { Instructions = instructionPrompt, Tools = this._tools };
    }

    private record AgentContent(string Name, string Description, string Instructions, AITool[] Tools, ReasoningOptions? Reasoning = null);

    private async Task<string> UseSubAgentAsync(
        [Description("子代理名称")] string name,
        [Description("子代理输入")] string input,
        CancellationToken cancellationToken = default
    )
    {
        if (!_agents.TryGetValue(name, out var content))
            return "Error: 未找到该子代理.";

        _logger.LogInformation(
            $"""
            {content.Name} request: 
            {input}
            """
        );

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
                    MaxOutputTokens = 10000,
                    Tools = content.Tools,
                    Reasoning = reasoning,
                },
                AIContextProviders = _providers,
            }
        );
        var session = await subAgent.CreateSessionAsync(cancellationToken);

        var response = await subAgent.RunAsync(input, session, cancellationToken: cancellationToken);

        _logger.LogInformation(
            $"""
            {content.Name} response: 
            {response}
            """
        );

        return string.IsNullOrEmpty(response.Text) ? "Error:未获取到输出结果" : response.Text;
    }
}
