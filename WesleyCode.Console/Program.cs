using WesleyCode.Agent.Extensions;
using WesleyCode.ConsoleHost.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.AddAgentHost(Directory.GetCurrentDirectory());
builder.Services.AddHostedService<ConsoleAgentHostedService>();
using var host = builder.Build();
await host.RunAsync();
