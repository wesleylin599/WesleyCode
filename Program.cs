using WesleyCode.Extensions;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.AddAgentHost(Directory.GetCurrentDirectory());
using var host = builder.Build();
await host.RunAsync();
