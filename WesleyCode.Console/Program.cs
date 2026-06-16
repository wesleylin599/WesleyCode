using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Services;
using WesleyCode.Console.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder.AddAgentHost(Directory.GetCurrentDirectory());
builder
    .Services.AddHttpClient()
    .ConfigureHttpClientDefaults(builder =>
    {
        builder.ConfigureHttpClient(c =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
    });
builder.Services.AddHostedService<ConsoleAgentHostedService>();
builder.Services.AddSingleton<IOutputCapture, ConsoleOutputCapture>();
using var host = builder.Build();
await host.RunAsync();
