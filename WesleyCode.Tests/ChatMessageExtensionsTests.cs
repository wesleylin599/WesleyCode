using Microsoft.Extensions.AI;
using WesleyCode.Agent.Extensions;

namespace WesleyCode.Tests;

/// <summary>
/// <see cref="ChatMessageExtensions.WithMessageId"/> 的单元测试。
/// </summary>
public class ChatMessageExtensionsTests
{
    [Fact]
    public void WithMessageId_SameId_ReturnsSameInstance()
    {
        var message = new ChatMessage(ChatRole.Assistant, "hello") { MessageId = "abc" };
        var result = message.WithMessageId("abc");
        Assert.Same(message, result);
    }

    [Fact]
    public void WithMessageId_DifferentId_ReturnsClonedInstanceWithNewId()
    {
        var message = new ChatMessage(ChatRole.Assistant, "hello") { MessageId = "old" };
        var result = message.WithMessageId("new");

        Assert.NotSame(message, result);
        Assert.Equal("new", result.MessageId);
        // 原始消息不变
        Assert.Equal("old", message.MessageId);
    }

    [Fact]
    public void WithMessageId_NullOriginalId_AssignsNewId()
    {
        var message = new ChatMessage(ChatRole.User, "hi");
        Assert.Null(message.MessageId);

        var result = message.WithMessageId("assigned");
        Assert.Equal("assigned", result.MessageId);
        Assert.NotSame(message, result);
    }

    [Fact]
    public void WithMessageId_PreservesContentAndRole()
    {
        var message = new ChatMessage(ChatRole.User, "hello world") { MessageId = "old" };
        var result = message.WithMessageId("new");

        Assert.Equal(ChatRole.User, result.Role);
        Assert.Equal("hello world", result.Text);
    }
}
