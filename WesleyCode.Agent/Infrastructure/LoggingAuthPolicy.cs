using System;
using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace WesleyCode.Agent.Infrastructure;

[DebuggerStepThrough]
internal class LoggingAuthPolicy : PipelinePolicy
{
    private readonly ILogger<LoggingAuthPolicy> _logger;

    public LoggingAuthPolicy(ILoggerFactory? loggerFactory = null)
    {
        this._logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<LoggingAuthPolicy>();
    }

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        LogRequest(message);
        ProcessNext(message, pipeline, currentIndex);
        LogResponse(message);
    }

    public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        await LogRequestAsync(message);
        await ProcessNextAsync(message, pipeline, currentIndex);
        await LogResponseAsync(message);
    }

    private void LogRequest(PipelineMessage message)
    {
        var request = message.Request;

        _logger.LogDebug(
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

            _logger.LogDebug(
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

        _logger.LogDebug(
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
            _logger.LogDebug(
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
