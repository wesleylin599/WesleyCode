using System.Text;
using UtfUnknown;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 提供 <see cref="MemoryStream"/> 的输出解码扩展方法。
/// </summary>
public static class StreamExtensions
{
    /// <summary>
    /// 自动检测字符编码并将流内容解码为字符串（去除尾部空白）。
    /// </summary>
    public static string DecodeOutput(this MemoryStream stream)
    {
        var bytes = stream.ToArray();

        if (bytes.Length == 0)
            return string.Empty;

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        var result = CharsetDetector.DetectFromBytes(bytes);

        var encoding = result.Detected?.Encoding ?? Encoding.UTF8;

        return encoding.GetString(bytes).TrimEnd();
    }
}
