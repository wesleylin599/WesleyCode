using System.Collections.Concurrent;
using System.ComponentModel;
using System.Security;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestConsole5;

internal class SubAgentProvider : AIContextProvider
{
    private const string DefaultInstructionPrompt = """
        你拥有以下的智能体,在需要的时候使用智能体去完成任务:

        <available_agents>
        {0}
        </available_agents>

        当任务有匹配的智能体时:
        1.使用 'use_subAgent' 调用子代理.
        2.总结执行的结果.

        子代理拥有独立的上下文.
        多步骤任务优先使用子代理.
        """;

    private static readonly AgentContent planner = new AgentContent(
        "planner",
        "规划代理,用于设计实现策略,任务清单需由执行代理获取",
        "你是一个规划代理.获取所需要的信息并将任务添加到任务队列中,每个任务需要可独立执行,列表添加完成输出已添加到任务清单即可.不要做更改.",
        [ToolManager.CommandFunction, ToolManager.ReadFileFunction, ToolManager.EnqueueTaskFunction, ToolManager.SelectTasksFunction]
    );
    private static readonly AgentContent executor = new AgentContent(
        "executor",
        "执行代理,用于执行任务实现功能,使用时传入`获取一条任务并完成`需要时传入一些已知信息,使用前先使用规划代理",
        "你是一个执行代理.从任务队列中获取任务,高效地实现请求的更改,更新任务清单的执行结果,完成输出已完成任务即可.",
        ToolManager.AllFunctions
    );
    private static readonly AgentContent reviewer = new AgentContent(
        "reviewer",
        "审阅代理,用于对任务完成度进行评估,执行代理使用后调用本代理",
        "你是一个审阅代理.从任务队列中获取任务,评估任务完成度,输出评估结论.不要做更改.",
        [ToolManager.CommandFunction, ToolManager.ReadFileFunction, ToolManager.SelectTasksFunction]
    );

    private static List<AgentContent> _agentContents = [planner, executor, reviewer];

    private readonly AITool[] _tools;
    private readonly IChatClient _client;
    private readonly string _workDirectory;
    private readonly AIContextProvider[] _providers;
    private readonly ILogger<SubAgentProvider> _logger;
    private readonly ConcurrentDictionary<string, AgentContent> _agents = new();

    public SubAgentProvider(string workDirectory, IChatClient client, AIContextProvider[]? providers = null, ILoggerFactory? loggerFactory = null)
    {
        _client = client;
        _workDirectory = workDirectory;
        _providers = providers ?? [];
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SubAgentProvider>();
        _tools = [AIFunctionFactory.Create(this.UseSubAgentAsync, name: "use_subAgent", description: "调用子代理,获取子代理执行结果.")];
    }

    protected override async ValueTask<AIContext> ProvideAIContextAsync(InvokingContext context, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();
        foreach (var agentContent in _agentContents)
        {
            var key = Guid.NewGuid().ToString();
            sb.AppendLine("  <agent>");
            sb.AppendLine($"    <key>{key}</key>");
            sb.AppendLine($"    <name>{SecurityElement.Escape(agentContent.Name)}</name>");
            sb.AppendLine($"    <description>{SecurityElement.Escape(agentContent.Description)}</description>");
            sb.AppendLine($"    <tools>");
            foreach (var tool in agentContent.Tools)
            {
                sb.AppendLine($"        <tool>");
                sb.AppendLine($"            <name>{SecurityElement.Escape(tool.Name)}</name>");
                sb.AppendLine($"            <description>{SecurityElement.Escape(tool.Description)}</description>");
                sb.AppendLine($"        </tool>");
            }
            sb.AppendLine($"    </tools>");
            sb.AppendLine("  </agent>");
            _agents[key] = agentContent;
        }

        var instructionPrompt = string.Format(DefaultInstructionPrompt, sb.ToString().TrimEnd());
        return new AIContext { Instructions = instructionPrompt, Tools = this._tools };
    }

    private record AgentContent(string Name, string Description, string Instructions, AITool[] Tools);

    private async Task<string> UseSubAgentAsync(
        [Description("子代理的key")] string key,
        [Description("子代理输入")] string input,
        CancellationToken cancellationToken = default
    )
    {
        if (!_agents.TryGetValue(key, out var content))
            return "Error: 未找到该子代理.";

        _logger.LogInformation($"{content.Name} request: {input}");

        AIAgent subAgent = _client.AsAIAgent(
            options: new ChatClientAgentOptions
            {
                Name = content.Name,
                Description = content.Description,
                ChatOptions = new ChatOptions
                {
                    Instructions = content.Instructions,
                    Tools = ToolManager.AllFunctions,
                    ToolMode = ChatToolMode.Auto,
                    AllowMultipleToolCalls = true,
                },
                AIContextProviders = _providers,
            }
        );
        var session = await subAgent.CreateSessionAsync(cancellationToken);

        var chatMeaasges = new List<ChatMessage>()
        {
            new ChatMessage(ChatRole.User, $"你是位于 {_workDirectory} 的子代理"),
            new ChatMessage(ChatRole.User, input),
        };

        var response = await subAgent.RunAsync(chatMeaasges, session, new() { ResponseFormat = ChatResponseFormat.Text }, cancellationToken);

        _logger.LogInformation($"{content.Name} response: {response}");

        return response.Text;
    }
}
