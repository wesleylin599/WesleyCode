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
}
