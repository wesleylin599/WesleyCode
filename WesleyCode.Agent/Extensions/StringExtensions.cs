using System.Security.Cryptography;
using System.Text;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 提供 <see cref="string"/> 的哈希等扩展方法。
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// 计算字符串的 UTF-8 编码后的 MD5 十六进制小写摘要。
    /// </summary>
    public static string ComputeMd5(this string target) => Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(target))).ToLowerInvariant();

    /// <summary>
    /// 从字符串移除一个或多个连续的 <paramref name="marker"/>，保留字符串中间出现的的标记。
    /// 若 <paramref name="marker"/> 为空或空白，则原样返回。
    /// </summary>
    public static string TrimMarker(this string target, string marker)
    {
        if (string.IsNullOrEmpty(marker))
        {
            return target;
        }

        return target.Replace(marker, string.Empty);
    }
}
