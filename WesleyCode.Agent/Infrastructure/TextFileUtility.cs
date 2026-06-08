using System.Text;

namespace WesleyCode.Agent.Infrastructure;

internal sealed record TextFileContent(string Content, Encoding Encoding, string EncodingName, string LineEnding);

internal static class TextFileUtility
{
    private static readonly UTF8Encoding Utf8BomEncoding = new(true);
    private static readonly UTF8Encoding Utf8Encoding = new(false);

    public static async Task<TextFileContent> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var content = await reader.ReadToEndAsync(cancellationToken);
        var encoding = reader.CurrentEncoding;
        return new TextFileContent(content, encoding, GetEncodingName(encoding), DetectLineEnding(content));
    }

    public static Encoding ResolveEncoding(string? encodingName, Encoding? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(encodingName) || string.Equals(encodingName, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return fallback ?? Utf8BomEncoding;
        }

        if (
            string.Equals(encodingName, "utf-8-bom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "utf8-bom", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "utf8bom", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Utf8BomEncoding;
        }

        if (
            string.Equals(encodingName, "utf-8", StringComparison.OrdinalIgnoreCase)
            || string.Equals(encodingName, "utf8", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Utf8Encoding;
        }

        return Encoding.GetEncoding(encodingName);
    }

    public static string GetEncodingName(Encoding encoding)
    {
        if (encoding is UTF8Encoding utf8Encoding)
        {
            return utf8Encoding.GetPreamble().Length > 0 ? "utf-8-bom" : "utf-8";
        }

        return encoding.WebName;
    }

    public static string DetectLineEnding(string content)
    {
        if (content.Contains("\r\n", StringComparison.Ordinal))
        {
            return "\r\n";
        }

        if (content.Contains('\n'))
        {
            return "\n";
        }

        return Environment.NewLine;
    }

    public static string NormalizeLineEndings(string content, string? lineEnding)
    {
        if (string.IsNullOrEmpty(lineEnding))
        {
            return content;
        }

        return content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", lineEnding, StringComparison.Ordinal);
    }

    public static async Task WriteAllTextAsync(string path, string content, Encoding encoding, CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var writer = new StreamWriter(path, append: false, encoding);
        await writer.WriteAsync(content.AsMemory(), cancellationToken);
    }
}
