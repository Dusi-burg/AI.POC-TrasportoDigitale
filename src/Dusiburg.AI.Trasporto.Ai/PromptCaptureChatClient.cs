using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Ai;

/// <summary>
/// Diagnostica (come in O2C e Sinistri): salva su file ogni richiesta al modello così come parte — istruzioni, messaggi,
/// tool offerti, opzioni — e la risposta ricevuta. Si attiva solo con <see cref="AiOptions.Keys.PromptCaptureDirectory"/>;
/// i file possono contenere dati di business, quindi vanno tenuti fuori dal repository.
/// </summary>
public sealed class PromptCaptureChatClient(IChatClient inner, string directory) : DelegatingChatClient(inner)
{
    private static readonly JsonSerializerOptions Output = new(AIJsonUtilities.DefaultOptions) { WriteIndented = true };

    private static int _sequence;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> request = [.. messages];
        ChatResponse response = await base.GetResponseAsync(request, options, cancellationToken);

        await WriteAsync(request, options, response, cancellationToken);

        return response;
    }

    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<ChatMessage> request = [.. messages];
        List<ChatResponseUpdate> updates = [];

        await foreach (ChatResponseUpdate update in base.GetStreamingResponseAsync(request, options, cancellationToken))
        {
            updates.Add(update);

            yield return update;
        }

        await WriteAsync(request, options, updates.ToChatResponse(), cancellationToken);
    }

    private async Task WriteAsync(IReadOnlyList<ChatMessage> request, ChatOptions? options, ChatResponse response, CancellationToken cancellationToken)
    {
        int sequence = Interlocked.Increment(ref _sequence);

        var capture = new JsonObject
        {
            ["sequence"] = sequence,
            ["capturedAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["options"] = new JsonObject
            {
                ["modelId"] = options?.ModelId,
                ["temperature"] = options?.Temperature,
                ["toolMode"] = options?.ToolMode?.ToString(),
                ["responseFormat"] = options?.ResponseFormat?.GetType().Name,
                ["additionalProperties"] = options?.AdditionalProperties is { } extra
                    ? JsonSerializer.SerializeToNode(extra.ToDictionary(p => p.Key, p => p.Value?.ToString()), Output)
                    : null
            },
            ["instructions"] = options?.Instructions,
            ["tools"] = new JsonArray([.. (options?.Tools ?? []).Select(DescribeTool)]),
            ["messages"] = new JsonArray([.. request.Select(DescribeMessage)]),
            ["response"] = new JsonArray([.. response.Messages.Select(DescribeMessage)]),
            ["finishReason"] = response.FinishReason?.Value,
            ["usage"] = JsonSerializer.SerializeToNode(response.Usage, Output)
        };

        Directory.CreateDirectory(directory);

        string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{sequence:D3}.json");

        await File.WriteAllTextAsync(path, capture.ToJsonString(Output), cancellationToken);
    }

    private static JsonNode DescribeTool(AITool tool) => tool switch
    {
        AIFunctionDeclaration function => new JsonObject
        {
            ["name"] = function.Name,
            ["description"] = function.Description,
            ["schema"] = JsonNode.Parse(function.JsonSchema.GetRawText())
        },
        _ => new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["kind"] = tool.GetType().Name }
    };

    private static JsonNode DescribeMessage(ChatMessage message) => new JsonObject
    {
        ["role"] = message.Role.Value,
        ["authorName"] = message.AuthorName,
        ["contents"] = new JsonArray([.. message.Contents.Select(DescribeContent)])
    };

    private static JsonNode DescribeContent(AIContent content) => content switch
    {
        TextContent text => new JsonObject { ["text"] = text.Text },
        FunctionCallContent call => new JsonObject
        {
            ["functionCall"] = call.Name,
            ["callId"] = call.CallId,
            ["arguments"] = JsonSerializer.SerializeToNode(call.Arguments, Output)
        },
        FunctionResultContent result => new JsonObject
        {
            ["functionResult"] = result.CallId,
            ["result"] = JsonSerializer.SerializeToNode(result.Result, Output)
        },
        _ => new JsonObject { ["content"] = content.GetType().Name }
    };
}
