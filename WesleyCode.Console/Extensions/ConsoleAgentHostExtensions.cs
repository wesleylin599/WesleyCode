using WesleyCode.Agent.Extensions;
using WesleyCode.ConsoleHost.Hosting;

namespace WesleyCode.ConsoleHost.Extensions;

public static class ConsoleAgentHostExtensions
{
    public static IHostApplicationBuilder AddConsoleAgentHost(this IHostApplicationBuilder builder, string workDirectory)
    {
        builder.AddAgentHost(workDirectory);
        builder.Services.AddHostedService<ConsoleAgentHostedService>();
        return builder;
    }
}
