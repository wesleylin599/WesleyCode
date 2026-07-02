using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using WesleyCode.Web.Interfaces;

namespace WesleyCode.Web.Services;

public sealed class WebOutputState : IWebOutputCaptureState
{
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _channels = new();
    private readonly AsyncLocal<string?> _currentChannel = new();

    public event Action<string>? ChannelChanged;

    public IDisposable BeginChannel(string channelId)
    {
        var previous = _currentChannel.Value;
        _currentChannel.Value = channelId;
        return new ChannelScope(this, previous);
    }

    public IReadOnlyList<ChatMessage> GetMessages(string channelId)
    {
        var channel = _channels.GetOrAdd(channelId, static _ => []);
        lock (channel)
        {
            return [.. channel];
        }
    }

    public void Reset(string channelId)
    {
        _channels[channelId] = [];
        ChannelChanged?.Invoke(channelId);
    }

    public void AddUserMessage(string channelId, string message) => this.AddMessage(channelId, ChatRole.User, "你", message);

    public void AddSystemMessage(string channelId, string message) => this.AddMessage(channelId, ChatRole.System, "系统", message);

    public void AddCurrentMessage(ChatRole role, string authorName, string message) =>
        this.AddMessage(_currentChannel.Value ?? "global", role, authorName, message);

    private void AddMessage(string channelId, ChatRole role, string authorName, string message)
    {
        var channel = _channels.GetOrAdd(channelId, static _ => []);
        lock (channel)
        {
            channel.Add(
                new ChatMessage(role, message.Trim())
                {
                    AuthorName = authorName,
                    CreatedAt = DateTimeOffset.Now,
                    MessageId = Guid.NewGuid().ToString("N"),
                }
            );
        }

        ChannelChanged?.Invoke(channelId);
    }

    private sealed class ChannelScope(WebOutputState owner, string? previousChannel) : IDisposable
    {
        public void Dispose()
        {
            owner._currentChannel.Value = previousChannel;
        }
    }
}
