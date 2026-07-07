using System.IO.Compression;
using Microsoft.Extensions.Options;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;
using WesleyCode.Agent.Options;
using WesleyCode.Web.Components;
using WesleyCode.Web.Interfaces;
using WesleyCode.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.ConfigureHttpClientAgents(client =>
{
    client.Timeout = Timeout.InfiniteTimeSpan;
});
builder.Services.AddScoped<ChatWorkspaceService>();
builder.Services.AddSingleton<IOutputCapture, WebOutputCapture>();
builder.Services.AddSingleton<IWebOutputCaptureState, WebOutputState>();
builder.Services.AddAgentHost(Path.Combine(AppContext.BaseDirectory, "workspace"));

var app = builder.Build();
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();
app.MapStaticAssets();
app.MapGet(
    "/workspace/archive",
    (IOptions<WorkingOptions> workingOptions) =>
    {
        var workspacePath = Path.GetFullPath(workingOptions.Value.BasePath);
        if (Directory.Exists(workspacePath))
        {
            using var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories))
                {
                    var entryPath = Path.GetRelativePath(workspacePath, filePath).Replace('\\', '/');
                    archive.CreateEntryFromFile(filePath, entryPath, CompressionLevel.Fastest);
                }
            }

            return Results.File(stream.ToArray(), "application/zip", $"workspace-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");
        }
        return Results.NotFound();
    }
);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
