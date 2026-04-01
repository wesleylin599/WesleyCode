using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Extensions.AI;

public sealed class CrsChatClient : DelegatingChatClient
{
    private const int MaxAttempts = 6;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(5);
    private const double JitterFactor = 0.25;
    private static readonly Regex StatusCodeRegex = new(@"\b([1-5][0-9]{2})\b", RegexOptions.Compiled);

    private readonly ILogger<CrsChatClient> _logger;

    public static CrsChatClient Create(IChatClient innerClient) => new CrsChatClient(innerClient, NullLogger<CrsChatClient>.Instance);

    public static CrsChatClient Create(IChatClient innerClient, ILogger<CrsChatClient> logger) => new CrsChatClient(innerClient, logger);

    private CrsChatClient(IChatClient innerClient, ILogger<CrsChatClient> logger)
        : base(innerClient)
    {
        _logger = logger ?? NullLogger<CrsChatClient>.Instance;
    }

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        List<ChatMessage> chatMessages = new();
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                var response = await this.GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);
                chatMessages.AddRange(response.Messages);
                return new ChatResponse(chatMessages);
            }
            catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                LogFailure(ex, null, "canceled", attempt, MaxAttempts);
                chatMessages.Add(new ChatMessage(ChatRole.Assistant, ex.Message));
                break;
            }
            catch (Exception ex)
            {
                var statusCode = GetStatusCode(ex);
                var transient = IsTransient(ex, statusCode, cancellationToken, out var reason);

                LogFailure(ex, statusCode, reason, attempt, MaxAttempts);

                if (!transient || attempt == MaxAttempts)
                {
                    chatMessages.Add(new ChatMessage(ChatRole.Assistant, ex.Message));
                    break;
                }

                var delay = GetDelay(attempt);
                try
                {
                    await Task.Delay(delay, cancellationToken);
                }
                catch (OperationCanceledException delayEx) when (cancellationToken.IsCancellationRequested)
                {
                    LogFailure(delayEx, null, "canceled", attempt, MaxAttempts);
                    chatMessages.Add(new ChatMessage(ChatRole.Assistant, delayEx.Message));
                    break;
                }
            }
        }

        return new ChatResponse(chatMessages);
    }

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        options ??= new ChatOptions();
        List<ChatMessage> request =
        [
            new ChatMessage(
                ChatRole.User,
                $"""
                <instructions>
                {options.Instructions}
                </instructions>
                """
            ),
        ];
        foreach (var message in messages)
            if (message.Role == ChatRole.System)
                request.Add(new ChatMessage(ChatRole.User, $"<system>{message.Text}</system>"));
            else
                request.Add(message);
        return base.GetStreamingResponseAsync(request, options, cancellationToken);
    }

    private static bool IsTransient(Exception ex, int? statusCode, CancellationToken cancellationToken, out string reason)
    {
        if (ex is OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                reason = "canceled";
                return false;
            }

            reason = "timeout";
            return true;
        }

        if (ex is TimeoutException)
        {
            reason = "timeout";
            return true;
        }

        if (ex is HttpRequestException)
        {
            if (statusCode is int code)
            {
                if (code == 408 || code == 429 || code >= 500)
                {
                    reason = "http_transient";
                    return true;
                }

                reason = "http_non_transient";
                return false;
            }

            reason = "http_transport";
            return true;
        }

        reason = "non_transient";
        return false;
    }

    private static int? GetStatusCode(Exception ex)
    {
        if (ex is HttpRequestException httpEx && httpEx.StatusCode.HasValue)
            return (int)httpEx.StatusCode.Value;

        var message = ex.Message;
        if (string.IsNullOrWhiteSpace(message))
            return null;

        var match = StatusCodeRegex.Match(message);
        if (match.Success && int.TryParse(match.Value, out var code))
            return code;

        return null;
    }

    private static TimeSpan GetDelay(int attempt)
    {
        var multiplier = Math.Pow(2, Math.Clamp(attempt - 1, 0, 10));
        var delayMs = Math.Min(MaxDelay.TotalMilliseconds, BaseDelay.TotalMilliseconds * multiplier);
        var jitterMs = delayMs * JitterFactor * Random.Shared.NextDouble();
        return TimeSpan.FromMilliseconds(delayMs + jitterMs);
    }

    private void LogFailure(Exception ex, int? statusCode, string reason, int attempt, int maxAttempts)
    {
        if (reason == "canceled")
        {
            _logger.LogWarning(ex, "CrsChatClient attempt {Attempt}/{MaxAttempts} canceled, status={StatusCode}", attempt, maxAttempts, statusCode);
            return;
        }

        if (attempt < maxAttempts)
        {
            _logger.LogWarning(
                ex,
                "CrsChatClient attempt {Attempt}/{MaxAttempts} failed ({Reason}), status={StatusCode}",
                attempt,
                maxAttempts,
                reason,
                statusCode
            );
        }
        else
        {
            _logger.LogError(
                ex,
                "CrsChatClient attempt {Attempt}/{MaxAttempts} failed ({Reason}), status={StatusCode}",
                attempt,
                maxAttempts,
                reason,
                statusCode
            );
        }
    }
}
