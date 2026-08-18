using Microsoft.Extensions.AI;

namespace WesleyCode.Agent.Extensions;

/// <summary>
/// 提供 <see cref="ChatMessage"/> 的扩展方法。
/// </summary>
public static class ChatMessageExtensions
{
    /// <summary>
    /// 若消息尚未设置指定 <paramref name="messageId"/>，则克隆并赋值后返回；否则返回原消息。
    /// </summary>
    public static ChatMessage WithMessageId(this ChatMessage message, string messageId)
    {
        if (message.MessageId != null && message.MessageId == messageId)
        {
            return message;
        }

        message = message.Clone();
        message.MessageId = messageId;
        return message;
    }
}
