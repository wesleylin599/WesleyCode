using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Services;
using WesleyCode.Console.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder
    .Configuration.SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});
builder
    .Services.AddHttpClient()
    .ConfigureHttpClientDefaults(builder =>
    {
        builder.ConfigureHttpClient(c =>
        {
            c.Timeout = Timeout.InfiniteTimeSpan;
        });
    });
builder.Services.AddSingleton<IOutputCapture, ConsoleOutputCapture>();
builder.Services.AddHostedService<ConsoleAgentHostedService>();
builder.Services.AddAgentHost(Directory.GetCurrentDirectory());
using var host = builder.Build();
await host.RunAsync();
