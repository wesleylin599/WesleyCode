using WesleyCode.Extensions;
using WesleyCode.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddSingleton<ConsoleLifecycle>();
builder.AddAgentHost(Directory.GetCurrentDirectory());

using var host = builder.Build();
using var lifecycle = host.Services.GetRequiredService<ConsoleLifecycle>();
await host.RunAsync(lifecycle.Token);
