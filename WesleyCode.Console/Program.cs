using WesleyCode.ConsoleHost.Extensions;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.AddConsoleAgentHost(Directory.GetCurrentDirectory());
using var host = builder.Build();
await host.RunAsync();
