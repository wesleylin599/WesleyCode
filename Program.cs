using System.ClientModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI;
using TestConsole5;

const string BaseUrl = "https://ccr.uu5c.top/openai";
const string ApiKey = "cr_666dc23101d921f599043b8a3eb1531bd1b73aea145f36aec87f526c8e818c5a";
const string ModelId = "gpt-5.2-codex";

using var cts = new CancellationTokenSource();

Process.GetCurrentProcess().Exited += Console_CancelKeyPress;
Console.CancelKeyPress += Console_CancelKeyPress;
void Console_CancelKeyPress(object? sender, EventArgs e) => cts.Cancel();

OpenAIClient openAI = new OpenAIClient(
    new ApiKeyCredential(ApiKey),
    new OpenAIClientOptions { Endpoint = new Uri(BaseUrl), MessageLoggingPolicy = new LoggingAuthPolicy(false, true) }
);

var baseClient = openAI.GetResponsesClient(ModelId).AsIChatClient();

var workDirectory = Directory.GetCurrentDirectory();

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.SetMinimumLevel(LogLevel.Debug);
    builder.AddConsole();
});

var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

var crsClient = CrsChatClient.Create(baseClient);

var pipeline = new PipelineCompactionStrategy(
    new ToolResultCompactionStrategy(CompactionTriggers.TokensExceed(1500)),
    new SummarizationCompactionStrategy(crsClient, CompactionTriggers.TokensExceed(10000)),
    new SlidingWindowCompactionStrategy(CompactionTriggers.TurnsExceed(10)),
    new TruncationCompactionStrategy(CompactionTriggers.TokensExceed(30000))
);
var compactionProvider = new CompactionProvider(pipeline, loggerFactory: loggerFactory);
var userSkills = Path.Combine(AppContext.BaseDirectory, "skills", "user");
var skillsProvider = new FileAgentSkillsProvider(
    [Path.Combine(AppContext.BaseDirectory, "skills", "system"), userSkills],
    new FileAgentSkillsProviderOptions()
    {
        SkillsInstructionPrompt = $"""
        You have access to skills containing domain-specific knowledge and capabilities.
        Each skill provides specialized instructions, reference documents, and assets for specific tasks.

        <available_skills>
        {0}
        </available_skills>

        When a task aligns with a skill's domain:
        1. Use `load_skill` to retrieve the skill's instructions
        2. Follow the provided guidance
        3. Use `read_skill_resource` to read any references or other files mentioned by the skill

        Only load what is needed, when it is needed.
        Put the newly added skills in the {userSkills} directory.
        """,
    },
    loggerFactory: loggerFactory
);
var agentProvider = new SubAgentProvider(workDirectory, crsClient, [compactionProvider, skillsProvider], loggerFactory);

var chatClient = crsClient.AsBuilder().UseDistributedCache(cache).UseLogging(loggerFactory).Build();

AIAgent agent = chatClient.AsAIAgent(
    options: new ChatClientAgentOptions
    {
        Name = "Assistant",
        ChatOptions = new ChatOptions
        {
            Instructions = $"""
            你是位于 {workDirectory} 的代理工具;
            通过命令行执行操作,专注任务使用子代理完成;
            给予你最高的权限不需要询问权限直接去操作;
            操作优先,使用工具获取你需要的信息;
            不要询问我,你去查询;
            """,
            Reasoning = new ReasoningOptions { Effort = ReasoningEffort.High },
            Tools = ToolManager.AllFunctions,
            ToolMode = ChatToolMode.Auto,
            AllowMultipleToolCalls = true,
        },
        AIContextProviders = [compactionProvider, skillsProvider, agentProvider],
    }
);

var sessionDir = Path.Combine(AppContext.BaseDirectory, "session");
var sessionFile = $"{MD5Encrypt16(workDirectory)}.json";
var sessionHistory = Path.Combine(sessionDir, sessionFile);

AgentSession session = File.Exists(sessionHistory)
    ? await agent.DeserializeSessionAsync(JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(sessionHistory)), cancellationToken: cts.Token)
    : await agent.CreateSessionAsync(cts.Token);

await PrintHistoryAsync(session);

while (true)
{
    try
    {
        Console.Write("> User : ");
        var input = Console.ReadLine();
        if (string.IsNullOrEmpty(input))
            continue;
        if (string.Equals(input, "/clear", StringComparison.OrdinalIgnoreCase))
        {
            Console.Clear();
            File.Delete(sessionHistory);
            session = await agent.CreateSessionAsync(cts.Token);
            continue;
        }
        if (string.Equals(input, "/exit", StringComparison.OrdinalIgnoreCase))
            break;
        using (CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cts.Token))
        {
            var task = CancelAgentAsync(source);
            var response = await agent.RunAsync(input, session, cancellationToken: source.Token);
            if (string.IsNullOrEmpty(response.Text))
            {
                StringBuilder stringBuilder = new StringBuilder();
                foreach (var message in response.Messages)
                foreach (var content in message.Contents)
                    if (content is ErrorContent error)
                        stringBuilder.AppendLine(error.Message);
                Console.WriteLine($"> Agent: {stringBuilder}");
            }
            else
                Console.WriteLine($"> Agent: {response}");
            source.Cancel();
            await task;
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"> Agent: {ex.Message}");
    }
    finally
    {
        JsonElement element = await agent.SerializeSessionAsync(session, cancellationToken: cts.Token);
        if (!Directory.Exists(sessionDir))
            Directory.CreateDirectory(sessionDir);
        await File.WriteAllTextAsync(sessionHistory, element.GetRawText(), cts.Token);
        Console.ResetColor();
    }
}

async Task PrintHistoryAsync(AgentSession activeSession)
{
    if (activeSession.TryGetInMemoryChatHistory(out var history) && history != null)
    {
        foreach (var message in history)
        {
            string target;
            if (string.IsNullOrEmpty(message.Text))
                continue;
            else if (message.Role == ChatRole.Tool)
                continue;
            else if (message.Role == ChatRole.User)
                target = $"> User : {message}";
            else if (message.Role == ChatRole.Assistant)
                target = $"> Agent: {message}";
            else
                target = $"> {message.Role}: {message}";
            cts.Token.ThrowIfCancellationRequested();
            Console.WriteLine(target);
        }
    }
    await Task.CompletedTask;
}

async Task CancelAgentAsync(CancellationTokenSource source)
{
    while (!source.IsCancellationRequested)
    {
        try
        {
            if (Console.KeyAvailable)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Escape)
                {
                    source.Cancel();
                }
            }
            await Task.Delay(50, source.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }
}

string MD5Encrypt16(string password)
{
    var md5 = MD5.Create();
    string t2 = BitConverter.ToString(md5.ComputeHash(Encoding.Default.GetBytes(password)), 4, 8);
    t2 = t2.Replace("-", string.Empty);
    return t2;
}
