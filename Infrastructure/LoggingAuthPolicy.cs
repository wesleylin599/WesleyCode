using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;

namespace Microsoft.Extensions.AI;

[DebuggerStepThrough]
internal class LoggingAuthPolicy : PipelinePolicy
{
    private readonly bool _logRequestBody;
    private readonly bool _logResponseBody;

    public LoggingAuthPolicy()
        : this(logRequestBody: true, logResponseBody: true) { }

    public LoggingAuthPolicy(bool logRequestBody, bool logResponseBody)
    {
        _logRequestBody = logRequestBody;
        _logResponseBody = logResponseBody;
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (_logRequestBody)
            LogRequest(message);
        ProcessNext(message, pipeline, currentIndex);
        if (_logResponseBody)
            LogResponse(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        if (_logRequestBody)
            await LogRequestAsync(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
        if (_logResponseBody)
            await LogResponseAsync(message);
    }

    private void LogRequest(PipelineMessage message)
    {
        var request = message.Request;

        Console.WriteLine($"URL: {request.Uri}");
        Console.WriteLine($"Method: {request.Method}");

        if (request.Content != null)
        {
            using var ms = new MemoryStream();
            request.Content.WriteTo(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());

            Console.WriteLine("Request Body:");
            Console.WriteLine(body);
            Console.WriteLine();
        }
    }

    private async Task LogRequestAsync(PipelineMessage message)
    {
        var request = message.Request;

        Console.WriteLine($"URL: {request.Uri}");
        Console.WriteLine($"Method: {request.Method}");

        if (request.Content != null)
        {
            using var ms = new MemoryStream();
            await request.Content.WriteToAsync(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());

            Console.WriteLine("Request Body:");
            Console.WriteLine(body);
            Console.WriteLine();
        }
    }

    private void LogResponse(PipelineMessage message)
    {
        if (message.Response is { IsError: true, ContentStream: not null })
        {
            using var ms = new MemoryStream();
            message.Response.ContentStream.CopyTo(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());
            throw new HttpRequestException(body);
        }
    }

    private async Task LogResponseAsync(PipelineMessage message)
    {
        if (message.Response is { IsError: true, ContentStream: not null })
        {
            using var ms = new MemoryStream();
            await message.Response.ContentStream.CopyToAsync(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());
            throw new HttpRequestException(body);
        }
    }
}
