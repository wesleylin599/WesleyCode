using WesleyCode.Extensions;
using WesleyCode.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Services.AddSingleton<ConsoleLifecycle>();
builder.AddAgentHost(Directory.GetCurrentDirectory());

using var host = builder.Build();
using var lifecycle = host.Services.GetRequiredService<ConsoleLifecycle>();
await host.RunAsync(lifecycle.Token);
