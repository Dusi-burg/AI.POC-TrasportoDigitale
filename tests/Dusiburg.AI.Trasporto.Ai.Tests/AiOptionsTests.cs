using Microsoft.Extensions.Configuration;

namespace Dusiburg.AI.Trasporto.Ai.Tests;

/// <summary>Manopole dei modelli: default del PLAN §8 e validazione dei valori.</summary>
public class AiOptionsTests
{
    [Test]
    public void FromConfiguration_Empty_UsesPlanDefaults()
    {
        //SUT
        AiOptions options = Options([]);

        Assert.That(options.OllamaEndpoint, Is.EqualTo(new Uri("http://127.0.0.1:11434")));
        Assert.That(options.OllamaChatModel, Is.EqualTo("qwen3.5:9b"));
        Assert.That(options.OllamaContextLength, Is.EqualTo(8_192));
        Assert.That(options.OllamaNumGpu, Is.EqualTo(99));
        Assert.That(options.AnthropicModel, Is.EqualTo("claude-haiku-5-5"));
        Assert.That(options.AnthropicJudgeModel, Is.EqualTo("claude-sonnet-5-5"));
        Assert.That(options.HasAnthropicApiKey, Is.False);
        Assert.That(options.EmbeddingModel, Is.EqualTo("embeddinggemma"));
        Assert.That(options.EmbeddingDimensions, Is.EqualTo(768));
        Assert.That(options.PromptCaptureDirectory, Is.Null);
    }

    [TestCase(ModelTask.Extraction, "ollama")]
    [TestCase(ModelTask.Mapping, "ollama")]
    [TestCase(ModelTask.Draft, "anthropic")]
    [TestCase(ModelTask.Judge, "anthropic")]
    public void ProviderFor_Default_LocalForRelease1CloudForRelease2(ModelTask task, string expected)
    {
        //SETUP
        AiOptions options = Options([]);

        //SUT
        Assert.That(options.ProviderFor(task), Is.EqualTo(expected));
    }

    [Test]
    public void ProviderFor_ConfiguredTask_ChangesOnlyThatTask()
    {
        //SETUP
        AiOptions options = Options(new() { ["MODEL_EXTRACTION_PROVIDER"] = " Anthropic " });

        //SUT
        Assert.That(options.ProviderFor(ModelTask.Extraction), Is.EqualTo(ModelProviders.Anthropic));
        Assert.That(options.ProviderFor(ModelTask.Mapping), Is.EqualTo(ModelProviders.Ollama));
    }

    [TestCase(ModelTask.Extraction, "claude-haiku-5-5")]
    [TestCase(ModelTask.Mapping, "claude-haiku-5-5")]
    [TestCase(ModelTask.Draft, "claude-haiku-5-5")]
    [TestCase(ModelTask.Judge, "claude-sonnet-5-5")]
    public void AnthropicModelFor_Task_JudgeUsesItsOwnModel(ModelTask task, string expected)
    {
        //SETUP
        AiOptions options = Options([]);

        //SUT
        Assert.That(options.AnthropicModelFor(task), Is.EqualTo(expected));
    }

    [TestCase("MODEL_MAPPING_PROVIDER", "openai")]
    [TestCase("OLLAMA_ENDPOINT", "localhost:11434")]
    [TestCase("OLLAMA_NUM_CTX", "512")]
    [TestCase("OLLAMA_NUM_GPU", "tutti")]
    [TestCase("EMBEDDING_DIMENSIONS", "4000")]
    [TestCase("EMBEDDING_DIMENSIONS", "settecento")]
    public void FromConfiguration_InvalidValue_ThrowsNamingTheKey(string key, string value)
    {
        //SUT
        Assert.That(() => Options(new() { [key] = value }), Throws.InvalidOperationException.With.Message.Contains(key));
    }

    private static AiOptions Options(Dictionary<string, string?> settings) =>
        AiOptions.FromConfiguration(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
}
