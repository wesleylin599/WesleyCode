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
    async (IOptions<WorkingOptions> workingOptions, HttpResponse response) =>
    {
        var workspacePath = Path.GetFullPath(workingOptions.Value.BasePath);
        if (!Directory.Exists(workspacePath))
        {
            return Results.NotFound();
        }

        var fileName = $"workspace-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip";
        response.Headers.ContentType = "application/zip";
        response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";

        await using (var archiveStream = new MemoryStream())
        {
            await using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var filePath in Directory.EnumerateFiles(workspacePath, "*", SearchOption.AllDirectories))
                {
                    var entryPath = Path.GetRelativePath(workspacePath, filePath).Replace('\\', '/');
                    archive.CreateEntryFromFile(filePath, entryPath, CompressionLevel.Fastest);
                }
            }

            // Reset to beginning and stream directly to response
            archiveStream.Position = 0;
            await archiveStream.CopyToAsync(response.Body);
        }
    }
);
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
