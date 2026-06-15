using System.Runtime.CompilerServices;

namespace WesleyCode.Console.Extensions;

internal static class LoggrtExtensions
{
    public static void LogEventId(this ILogger logger, string message, [CallerLineNumber] int lineNumber = 0) =>
        logger.LogError(new EventId(lineNumber), message);
}
