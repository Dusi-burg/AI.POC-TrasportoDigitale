using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dusiburg.AI.Trasporto.Ai.Tests;

/// <summary>Mascheramento dei dati personali verso il cloud: che cosa si maschera, che cosa no, e a chi si applica.</summary>
public class PiiMaskingChatClientTests
{
    private static CancellationToken CancellationToken => TestContext.CurrentContext.CancellationToken;

    [TestCase("Scrivere a mario.rossi@example.com per il ritiro.", "Scrivere a [email] per il ritiro.")]
    [TestCase("Referente: tel. 045 8001234, orario 8-12.", "Referente: tel. [telefono], orario 8-12.")]
    [TestCase("Chiamare il +39 347 1234567 all'arrivo.", "Chiamare il [telefono] all'arrivo.")]
    [TestCase("Cell: 3471234567", "Cell: [telefono]")]
    [TestCase("Phone +49-711-555-0199 / ordini@brambilla-om.it", "Phone [telefono] / [email]")]
    public void Mask_EmailsAndPhones_BecomePlaceholders(string text, string expected)
    {
        //SUT
        string masked = PiiMaskingChatClient.Mask(text);

        Assert.That(masked, Is.EqualTo(expected));
    }

    [TestCase("Merce ADR: UN 2014 PEROSSIDO DI IDROGENO, classe 5.1 (8), GI II.")]
    [TestCase("Consegna presso Papeteries du Rhône, 69007 Lyon, TVA FR99999000022. Peso lordo complessivo 8.840 kg.")]
    [TestCase("Partita IVA IT01000010007, targa ZZ104AA, CMR-1004001, 2026-10-08.")]
    [TestCase("UIL it-gate-sim:dusiburg-trasporto:01a10c7d-7306-77ea-ad13-29ade0157fbd, 202610040830+0100")]
    public void Mask_BusinessDataTheModelNeeds_IsLeftAsItIs(string text)
    {
        //SUT
        string masked = PiiMaskingChatClient.Mask(text);

        Assert.That(masked, Is.EqualTo(text));
    }

    [Test]
    public async Task GetResponse_MasksMessagesAndToolResultsOfTheCopySentToTheModel_NotTheOriginals()
    {
        //SETUP
        var inner = new CapturingChatClient();
        using var client = new PiiMaskingChatClient(inner);
        JsonElement toolResult = JsonSerializer.SerializeToElement(new { senderInstructions = "Avvisare +39 347 1234567 (anna@example.org). UN 2014." });
        ChatMessage[] conversation =
        [
            new(ChatRole.User, "Spedizione SH-1002, referente tel 045 8001234."),
            new(ChatRole.Tool, [new FunctionResultContent("call-1", toolResult)]),
            new(ChatRole.User, "Nessun dato personale qui.")
        ];

        //SUT
        await client.GetResponseAsync(conversation, cancellationToken: CancellationToken);

        string sentToolResult = inner.Received[1].Contents.OfType<FunctionResultContent>().Single().Result!.ToString()!;
        Assert.That(inner.Received[0].Text, Is.EqualTo("Spedizione SH-1002, referente tel [telefono]."));
        Assert.That(sentToolResult, Does.Contain("[telefono]").And.Contain("[email]").And.Contain("UN 2014"));
        Assert.That(sentToolResult, Does.Not.Contain("347").And.Not.Contain("anna@"));
        Assert.That(inner.Received[2], Is.SameAs(conversation[2]), "un messaggio senza dati personali passa così com'è");
        Assert.That(conversation[0].Text, Does.Contain("045 8001234"), "la conversazione dell'agente conserva i dati veri");
        Assert.That(conversation[1].Contents.OfType<FunctionResultContent>().Single().Result, Is.EqualTo(toolResult));
    }

    [Test]
    public void Factory_MasksOnlyTowardsTheCloud()
    {
        //SETUP
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ANTHROPIC_API_KEY"] = "test-key", ["MODEL_AUTHORITY_PROVIDER"] = "anthropic" })
            .Build();
        var factory = new ChatClientFactory(AiOptions.FromConfiguration(configuration), NullLoggerFactory.Instance);

        //SUT
        using ChatModel cloud = factory.Create(ModelTask.Authority);
        using ChatModel local = factory.Create(ModelTask.Mapping);

        Assert.That((cloud.Provider, local.Provider), Is.EqualTo((ModelProviders.Anthropic, ModelProviders.Ollama)));
        Assert.That(cloud.ChatClient.GetService<PiiMaskingChatClient>(), Is.Not.Null);
        Assert.That(local.ChatClient.GetService<PiiMaskingChatClient>(), Is.Null, "in locale i dati non lasciano la macchina");
        Assert.That(AiOptions.DefaultProviderFor(ModelTask.Authority), Is.EqualTo(ModelProviders.Ollama));
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public List<ChatMessage> Received { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Received.AddRange(messages);

            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "ok")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
