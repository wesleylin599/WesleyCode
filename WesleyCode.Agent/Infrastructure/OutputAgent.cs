using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using WesleyCode.Agent.Extensions;
using WesleyCode.Agent.Interfaces;

namespace WesleyCode.Agent.Infrastructure;

public sealed class OutputAgent : DelegatingAIAgent
{
    private readonly IOutputCapture _capture;

    public OutputAgent(AIAgent innerAgent, IOutputCapture capture)
        : base(innerAgent)
    {
        _capture = capture;
    }

    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        var response = await base.RunCoreAsync(messages, session, options, cancellationToken);
        foreach (var message in response.Messages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (message.Role == ChatRole.User && !string.IsNullOrEmpty(message.Text))
            {
                _capture.WriteUserMessage(message.Text);
            }
            else if (message.Role == ChatRole.System && !string.IsNullOrEmpty(message.Text))
            {
                _capture.WriteSystemMessage(message.Text);
            }
            else
            {
                foreach (var content in message.Contents)
                {
                    if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                    {
                        _capture.WriteAgentMessage(textContent.Text);
                    }

                    _capture.CommonWriteMessage(message.AuthorName, content);
                }
            }
        }
        return response;
    }

    protected override async IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        bool currentMove = true;
        StringBuilder currentBuilder = new();
        await using IAsyncEnumerator<AgentResponseUpdate> enumerator = base.RunCoreStreamingAsync(messages, session, options, cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (currentMove)
        {
            currentMove = await enumerator.MoveNextAsync();
            AgentResponseUpdate responseUpdate = currentMove switch
            {
                false => new(ChatRole.Assistant, [new ContinueContent()]),
                true => enumerator.Current,
            };
            if (responseUpdate.Role == ChatRole.User && !string.IsNullOrEmpty(responseUpdate.Text))
            {
                _capture.WriteUserMessage(responseUpdate.Text);
            }
            else if (responseUpdate.Role == ChatRole.System && !string.IsNullOrEmpty(responseUpdate.Text))
            {
                _capture.WriteSystemMessage(responseUpdate.Text);
            }
            else
            {
                foreach (var content in responseUpdate.Contents)
                {
                    if (content is TextContent textContent)
                    {
                        currentBuilder.Append(textContent.Text);
                    }
                    else if (currentBuilder.Length > 0)
                    {
                        _capture.WriteAgentMessage(currentBuilder.ToString());
                        currentBuilder.Clear();
                    }
                    _capture.CommonWriteMessage(responseUpdate.AuthorName, content);
                }
            }
            yield return responseUpdate;
        }
    }
}
