using WesleyCode.Extensions;
using WesleyCode.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.Logging.SetMinimumLevel(LogLevel.Information);
builder.Logging.AddFilter("OpenAI", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
builder.Logging.AddFilter("System.ClientModel", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Extensions.AI", LogLevel.Warning);
builder.Logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
builder.Services.AddSingleton<ConsoleLifecycle>();
builder.AddAgentHost(Directory.GetCurrentDirectory());

using var host = builder.Build();
using var lifecycle = host.Services.GetRequiredService<ConsoleLifecycle>();
await host.RunAsync(lifecycle.Token);
