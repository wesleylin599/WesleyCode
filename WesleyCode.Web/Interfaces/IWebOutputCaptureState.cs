using Microsoft.Extensions.AI;

namespace WesleyCode.Web.Interfaces;

public interface IWebOutputCaptureState
{
    event Action<string>? ChannelChanged;

    IDisposable BeginChannel(string channelId);

    IReadOnlyList<ChatMessage> GetMessages(string channelId);

    void Reset(string channelId);

    void AddUserMessage(string channelId, string message);

    void AddSystemMessage(string channelId, string message);

    void AddCurrentMessage(ChatRole role, string authorName, string message);
}