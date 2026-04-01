using TestConsole5.Extensions;
using TestConsole5.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole();
builder.Services.AddSingleton<ConsoleLifecycle>();
builder.Services.AddAgentHost(builder.Configuration, Directory.GetCurrentDirectory());

using var host = builder.Build();
using var lifecycle = host.Services.GetRequiredService<ConsoleLifecycle>();
await host.RunAsync(lifecycle.Token);
