using System.Runtime.CompilerServices;
using Dusiburg.AI.Trasporto.Ai;
using Microsoft.Extensions.AI;

namespace Dusiburg.AI.Trasporto.Agents.Tests.Support;

/// <summary>Modello a copione: ogni richiesta consuma il passo successivo; nessuna chiamata reale.</summary>
internal sealed class ScriptedChatClient(params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] steps) : IChatClient
{
    private int _next;

    public List<ChatOptions?> Options { get; } = [];

    /// <summary>I messaggi ricevuti a ogni richiesta: ciò che il modello ha visto.</summary>
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];

    public int RequestCount => _next;

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> request = [.. messages];

        Options.Add(options);
        Requests.Add(request);

        if (_next >= steps.Length)
        {
            throw new InvalidOperationException("Copione del modello esaurito.");
        }

        return Task.FromResult(steps[_next++](request));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ChatResponse response = await GetResponseAsync(messages, options, cancellationToken);

        foreach (ChatResponseUpdate update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }

    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Call(string toolName, Dictionary<string, object?> arguments) =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent($"call-{Guid.NewGuid():N}", toolName, arguments)]));

    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Text(string text) =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, text))
        {
            Usage = new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 }
        };

    /// <summary>Il modello si rifiuta di rispondere: <c>stop_reason = refusal</c> arriva come <c>ContentFilter</c>.</summary>
    public static Func<IReadOnlyList<ChatMessage>, ChatResponse> Refusal() =>
        _ => new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)) { FinishReason = ChatFinishReason.ContentFilter };

    /// <summary>Il testo dell'ultimo risultato di tool che il modello ha ricevuto in quella richiesta.</summary>
    public string LastToolResult(int requestIndex) =>
        Requests[requestIndex].SelectMany(m => m.Contents).OfType<FunctionResultContent>().Last().Result?.ToString() ?? string.Empty;
}

/// <summary>Fabbrica dei modelli che restituisce sempre il modello a copione, qualunque sia il compito.</summary>
/// <param name="provider">Il provider che il modello dichiara: di norma <c>stub</c>; uno vero per provare ciò che dipende dal provider.</param>
internal sealed class ScriptedChatClientFactory(IChatClient chatClient, string provider = "stub") : IChatClientFactory
{
    public List<ModelTask> Tasks { get; } = [];

    public ChatModel Create(ModelTask task)
    {
        Tasks.Add(task);

        return new ChatModel(chatClient, task, provider, "scripted", new ChatOptions());
    }
}
