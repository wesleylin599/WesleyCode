using System.Text;
using WesleyCode.Agent.Extensions;

namespace WesleyCode.Tests;

/// <summary>
/// <see cref="StringExtensions.ComputeMd5"/> 与 <see cref="StreamExtensions.DecodeOutput"/> 的单元测试。
/// </summary>
public class ExtensionMethodTests
{
    [Theory]
    [InlineData("", "d41d8cd98f00b204e9800998ecf8427e")]
    [InlineData("abc", "900150983cd24fb0d6963f7d28e17f72")]
    [InlineData("hello", "5d41402abc4b2a76b9719d911017c592")]
    public void ComputeMd5_ReturnsExpectedDigest(string input, string expected)
    {
        Assert.Equal(expected, input.ComputeMd5());
    }

    [Fact]
    public void ComputeMd5_IsDeterministic()
    {
        const string input = "sample-content";
        Assert.Equal(input.ComputeMd5(), input.ComputeMd5());
    }

    [Fact]
    public void ComputeMd5_ReturnsLowerCaseHex()
    {
        const string input = "WesleyCode";
        var digest = input.ComputeMd5();
        Assert.Equal(digest, digest.ToLowerInvariant());
        Assert.Equal(32, digest.Length);
    }

    [Fact]
    public void DecodeOutput_EmptyStream_ReturnsEmptyString()
    {
        using var stream = new MemoryStream();
        Assert.Equal(string.Empty, stream.DecodeOutput());
    }

    [Fact]
    public void DecodeOutput_Utf8Content_TrimsTrailingWhitespaceOnly()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("  你好，世界！  "));
        Assert.Equal("  你好，世界！", stream.DecodeOutput());
    }

    [Fact]
    public void DecodeOutput_PlainAscii_ReturnsTrimmedText()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("line1\r\nline2\r\n"));
        Assert.Equal("line1\r\nline2", stream.DecodeOutput());
    }
}
