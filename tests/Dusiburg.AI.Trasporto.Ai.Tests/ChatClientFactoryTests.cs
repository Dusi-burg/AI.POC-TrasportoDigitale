using Dusiburg.AI.Trasporto.Ai.Ollama;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OllamaSharp.Models;

namespace Dusiburg.AI.Trasporto.Ai.Tests;

/// <summary>Client dei modelli creati solo da configurazione; nessuna chiamata a Ollama o ad Anthropic.</summary>
public class ChatClientFactoryTests
{
    [Test]
    public void Create_LocalTask_UsesOllamaWithLowTemperatureAndConfiguredContext()
    {
        //SETUP
        ChatClientFactory factory = Factory(new() { ["OLLAMA_NUM_CTX"] = "12288" });

        //SUT
        using ChatModel model = factory.Create(ModelTask.Extraction);

        Assert.That((model.Task, model.Provider, model.ModelId), Is.EqualTo((ModelTask.Extraction, ModelProviders.Ollama, "qwen3.5:9b")));
        Assert.That(model.DefaultOptions.Temperature, Is.EqualTo(0.1f));
        Assert.That(model.DefaultOptions.AdditionalProperties?[OllamaOption.Think.Name], Is.EqualTo(false));
        Assert.That(model.DefaultOptions.AdditionalProperties?[OllamaOption.NumCtx.Name], Is.EqualTo(12_288));
        Assert.That(model.DefaultOptions.AdditionalProperties?[OllamaOption.NumGpu.Name], Is.EqualTo(AiOptions.DefaultOllamaNumGpu));
    }

    [Test]
    public void Create_LocalTaskWithNumGpuAuto_LeavesChoiceToOllama()
    {
        //SETUP
        ChatClientFactory factory = Factory(new() { ["OLLAMA_NUM_GPU"] = AiOptions.AutomaticNumGpu });

        //SUT
        using ChatModel model = factory.Create(ModelTask.Extraction);

        Assert.That(model.DefaultOptions.AdditionalProperties?.ContainsKey(OllamaOption.NumGpu.Name), Is.False);
    }

    [Test]
    public void Create_CloudTaskOnHaiku45_KeepsLowTemperature()
    {
        //SETUP
        ChatClientFactory factory = Factory(new()
        {
            ["ANTHROPIC_API_KEY"] = "test-key", ["ANTHROPIC_MODEL"] = "claude-haiku-4-5", ["MODEL_MAPPING_PROVIDER"] = "anthropic"
        });

        //SUT
        using ChatModel model = factory.Create(ModelTask.Mapping);

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Anthropic, "claude-haiku-4-5")));
        Assert.That(model.DefaultOptions.Temperature, Is.EqualTo(0.1f));
    }

    [Test]
    public void Create_CloudTaskOnDefaultHaiku55_SendsNoSampling()
    {
        //SETUP
        ChatClientFactory factory = Factory(new() { ["ANTHROPIC_API_KEY"] = "test-key", ["MODEL_MAPPING_PROVIDER"] = "anthropic" });

        //SUT
        using ChatModel model = factory.Create(ModelTask.Mapping);

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Anthropic, "claude-haiku-5-5")));
        Assert.That(model.DefaultOptions.Temperature, Is.Null, "Haiku 5.5 rifiuta una temperatura diversa dal default");
    }

    [Test]
    public void Create_Judge_UsesJudgeModelWithoutSampling()
    {
        //SETUP
        ChatClientFactory factory = Factory(new() { ["ANTHROPIC_API_KEY"] = "test-key" });

        //SUT
        using ChatModel model = factory.Create(ModelTask.Judge);

        Assert.That((model.Provider, model.ModelId), Is.EqualTo((ModelProviders.Anthropic, "claude-sonnet-5-5")));
        Assert.That(model.DefaultOptions.Temperature, Is.Null);
    }

    [Test]
    public void Create_CloudTaskWithoutKey_ThrowsNamingKeyAndKnob()
    {
        //SETUP
        ChatClientFactory factory = Factory([]);

        //SUT
        Assert.That(
            () => factory.Create(ModelTask.Draft),
            Throws.InvalidOperationException.With.Message.Contains("ANTHROPIC_API_KEY").And.Message.Contains("MODEL_DRAFT_PROVIDER"));
    }

    [Test]
    public void Create_WithCaptureDirectory_AddsPromptCapture()
    {
        //SETUP
        ChatClientFactory factory = Factory(new() { ["TRASPORTO_PROMPT_CAPTURE_DIR"] = Path.GetTempPath() });

        //SUT
        using ChatModel withCapture = factory.Create(ModelTask.Extraction);
        using ChatModel withoutCapture = Factory([]).Create(ModelTask.Extraction);

        Assert.That(withCapture.ChatClient.GetService<PromptCaptureChatClient>(), Is.Not.Null);
        Assert.That(withoutCapture.ChatClient.GetService<PromptCaptureChatClient>(), Is.Null);
    }

    [TestCase("claude-haiku-4-5", true, AnthropicThinkingMode.Extended)]
    [TestCase("claude-haiku-4-5-20251001", true, AnthropicThinkingMode.Extended)]
    [TestCase("claude-haiku-5-5", false, AnthropicThinkingMode.Adaptive)]
    [TestCase("claude-sonnet-5-5", false, AnthropicThinkingMode.Adaptive)]
    [TestCase("claude-opus-5-5", false, AnthropicThinkingMode.Adaptive)]
    [TestCase("claude-modello-futuro", false, AnthropicThinkingMode.Adaptive)]
    public void AnthropicModelRules_Model_SamplingOnlyOnHaiku(string model, bool acceptsTemperature, AnthropicThinkingMode thinkingMode)
    {
        //SUT
        Assert.That(ChatClientFactory.AcceptsTemperature(model), Is.EqualTo(acceptsTemperature));
        Assert.That(ChatClientFactory.ThinkingModeFor(model), Is.EqualTo(thinkingMode));
    }

    [Test]
    public void EmbeddingFactory_Default_MetadataFromConfiguredModel()
    {
        //SETUP
        var factory = new EmbeddingGeneratorFactory(Options([]), NullLoggerFactory.Instance);

        //SUT
        using IEmbeddingGenerator<string, Embedding<float>> generator = factory.Create();

        EmbeddingGeneratorMetadata? metadata = generator.GetService<EmbeddingGeneratorMetadata>();
        Assert.That(metadata?.DefaultModelId, Is.EqualTo("embeddinggemma"));
        Assert.That(metadata?.DefaultModelDimensions, Is.EqualTo(768));
    }

    [TestCase("embeddinggemma:latest", "embeddinggemma", true)]
    [TestCase("embeddinggemma:latest", "EmbeddingGemma:latest", true)]
    [TestCase("qwen3.5:9b", "qwen3.5", false)]
    [TestCase("qwen3.5:9b", "qwen3.5:9b", true)]
    public void SameModel_ImplicitLatestTag(string installed, string configured, bool expected)
    {
        //SUT
        Assert.That(OllamaClients.SameModel(installed, configured), Is.EqualTo(expected));
    }

    private static ChatClientFactory Factory(Dictionary<string, string?> settings) => new(Options(settings), NullLoggerFactory.Instance);

    private static AiOptions Options(Dictionary<string, string?> settings) =>
        AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
}
