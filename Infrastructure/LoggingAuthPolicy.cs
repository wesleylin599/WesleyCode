using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace WesleyCode.Infrastructure;

[DebuggerStepThrough]
internal class LoggingAuthPolicy : PipelinePolicy
{
    private readonly bool _logRequestBody;
    private readonly bool _logResponseBody;
    private readonly ILogger<LoggingAuthPolicy> _logger;

    public LoggingAuthPolicy(bool logRequestBody, bool logResponseBody, ILoggerFactory? loggerFactory = null)
    {
        _logRequestBody = logRequestBody;
        _logResponseBody = logResponseBody;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LoggingAuthPolicy>();
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

        _logger.LogInformation(
            $"""
            URL: {request.Uri}
            Method: {request.Method}
            """
        );

        if (request.Content != null)
        {
            using var ms = new MemoryStream();
            request.Content.WriteTo(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());

            _logger.LogInformation(
                $"""
                Request Body:
                {body}
                """
            );
        }
    }

    private async Task LogRequestAsync(PipelineMessage message)
    {
        var request = message.Request;

        _logger.LogInformation(
            $"""
            URL: {request.Uri}
            Method: {request.Method}
            """
        );

        if (request.Content != null)
        {
            using var ms = new MemoryStream();
            await request.Content.WriteToAsync(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());

            _logger.LogInformation(
                $"""
                Request Body:
                {body}
                """
            );
        }
    }

    private void LogResponse(PipelineMessage message)
    {
        if (message.Response is { IsError: true, ContentStream: not null })
        {
            using var ms = new MemoryStream();
            message.Response.ContentStream.CopyTo(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());
            var preview = CreateSafePreview(body);
            _logger.LogWarning("HTTP error response body (preview): {Preview}", preview);
        }
    }

    private async Task LogResponseAsync(PipelineMessage message)
    {
        if (message.Response is { IsError: true, ContentStream: not null })
        {
            using var ms = new MemoryStream();
            await message.Response.ContentStream.CopyToAsync(ms);
            var body = Encoding.UTF8.GetString(ms.ToArray());
            var preview = CreateSafePreview(body);
            _logger.LogWarning("HTTP error response body (preview): {Preview}", preview);
        }
    }

    private static string CreateSafePreview(string body)
    {
        const int maxLength = 512;
        if (string.IsNullOrWhiteSpace(body))
        {
            return "(empty)";
        }

        var compact = body.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
        return compact.Length <= maxLength ? compact : $"{compact[..maxLength]}{{truncated}}";
    }
}
