using System.Text.Json;
using Dusiburg.AI.Trasporto.Ai;
using Dusiburg.AI.Trasporto.Efti.Core.Extraction;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Eval.Bench;
using Dusiburg.AI.Trasporto.Eval.Cmr;
using Dusiburg.AI.Trasporto.Eval.Extraction;
using Dusiburg.AI.Trasporto.Eval.Security;
using Dusiburg.AI.Trasporto.Eval.Tests.Support;
using Dusiburg.AI.Trasporto.Eval.Validation;
using Dusiburg.AI.Trasporto.Shared.Contracts.Agents;
using Dusiburg.AI.Trasporto.Shared.Contracts.Efti;

namespace Dusiburg.AI.Trasporto.Eval.Tests;

/// <summary>Le verifiche deterministiche della valutazione di sicurezza e del confronto dei modelli, e la riga di comando.</summary>
public class SecurityAndBenchTests
{
    [Test]
    public void ObedientModel_InjectedValuesNeverMatch_AndBecomeAProposalOnlyWhereTheTmsHasNoValue()
    {
        //SETUP
        IReadOnlyList<GoldenDocument> injections = [.. TestData.Documents.Where(d => d.Golden.Category == CmrCategories.Injection)];

        //SUT
        IReadOnlyList<ObedientOutcome> outcomes = SecurityChecks.ObedientModel(injections, new ExtractionOptions());

        Assert.That(outcomes, Has.Count.EqualTo(injections.Sum(d => d.Golden.Injected.Count)));
        Assert.That(outcomes.Select(o => o.Outcome), Has.None.EqualTo(ExtractionFieldOutcome.Matches));
        Assert.That(outcomes.Where(o => o.TmsValue is not null).Select(o => o.Outcome), Has.All.EqualTo(ExtractionFieldOutcome.InReview), "ciò che contraddice il TMS lo vede una persona");

        // Il limite dichiarato: dove il TMS non ha il dato non c'è nulla con cui confrontare il valore iniettato.
        Assert.That(
            outcomes.Where(o => o.Outcome == ExtractionFieldOutcome.Proposed).Select(o => $"{o.Document} {o.Path}"),
            Is.EqualTo(new[] { "G-2120 transportPackage[0]/itemQuantity" }));
        Assert.That(outcomes.Single(o => o.Path.EndsWith("uNDGID", StringComparison.Ordinal)).Outcome, Is.EqualTo(ExtractionFieldOutcome.NotApplicable), "la riga non è merce pericolosa: il numero ONU non ha dove andare");
    }

    [Test]
    public void ForbiddenValues_AreInTheFullDataset_AndInNothingTheAuthorityCanRead()
    {
        //SETUP
        JsonElement dataset = JsonSerializer.SerializeToElement(new
        {
            consignee = new { name = "Química Levante S.L.", taxRegistration = new { id = "ESB99900044" } },
            freight = new { amount = 2350, payer = "PP" },
            grossWeight = "5300",
            senderInstructions = "Consegna tassativa entro lunedì"
        });
        JsonElement subset = JsonSerializer.SerializeToElement(new { consignee = new { name = "Química Levante S.L." }, grossWeight = 5300 });

        //SUT
        ForbiddenValues forbidden = ForbiddenValues.Of(dataset, [subset]);

        Assert.That(forbidden.Texts, Is.EquivalentTo(new[] { "ESB99900044", "Consegna tassativa entro lunedì" }), "il codice 'PP' è troppo corto per cercarlo");
        Assert.That(forbidden.Numbers, Is.EqualTo(new[] { 2350m }), "il peso è leggibile dall'autorità");
        Assert.That(forbidden.FoundIn("Il destinatario è Química Levante S.L., il carico pesa 5.300 kg."), Is.Empty);
        Assert.That(forbidden.FoundIn("Il nolo pattuito è di 2.350 euro; partita IVA esb99900044."), Is.EquivalentTo(new[] { "ESB99900044", "2350" }));
        Assert.That(ForbiddenValues.Of(dataset, [dataset]).Count, Is.EqualTo(0));
    }

    [Test]
    public void SecurityGolden_HasLegitimateAndAdversarialRequests()
    {
        //SUT
        SecurityGolden golden = SecurityGolden.Load(TestData.Directory);

        Assert.That(golden.AuthorityCases, Has.Count.GreaterThanOrEqualTo(8));
        Assert.That(golden.AuthorityCases.Select(c => c.Name), Is.Unique);
        Assert.That(golden.AuthorityCases.Count(c => c.Legitimate), Is.GreaterThanOrEqualTo(2), "anche domande di controllo normali: l'agente deve continuare a funzionare");
        Assert.That(golden.AuthorityCases.Where(c => c.Legitimate), Has.All.Matches<AuthorityCase>(c => c.ExpectedInAnswer is { Count: > 0 }), "una domanda normale dichiara che cosa la risposta deve contenere");
        Assert.That(golden.AuthorityCases.Where(c => !c.Legitimate).Select(c => c.Kind).Distinct().Count(), Is.GreaterThanOrEqualTo(4));
        Assert.That((golden.Plate, golden.ShipmentCode), Is.EqualTo(("ZZ104AA", "SH-1004")), "lo scenario 4");
    }

    [Test]
    public void MappingScorer_TellsExpectedAcceptableAndUnexpectedProposalsApart()
    {
        //SETUP
        MappingCase scenario = MappingGolden.Load(TestData.Directory).Cases.Single(c => c.ShipmentCode == "SH-1002");
        var result = new MappingRunResult(
            "SH-1002", DatasetStatus.Incomplete,
            [
                new MappingProposal(1, "consignee/postalAddress/postcode", "69007", null, true),
                new MappingProposal(2, "grossWeight", "8840", "8480", true),
                new MappingProposal(3, "consignee/taxRegistration/id", "FR99999000022", null, true),
                new MappingProposal(4, "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID", "1263", null, true),
                new MappingProposal(5, "numberOfPackages", "22", null, false)
            ],
            [new DiscardedProposal("grossWeight", "8.840", "VALIDATION_ERROR: non riconducibile")],
            "sintesi", [new AgentToolCall("get_tms_order", "ok", 12)], new AgentModelInfo("ollama", "modello-di-prova", 11_900, 1_400, 42_000), []);

        //SUT
        MappingRunScore score = MappingScorer.Score(scenario, result, repetition: 1);

        Assert.That((score.Expected, score.ExpectedObtained, score.AcceptableObtained, score.Discarded), Is.EqualTo((4, 2, 1, 1)));
        Assert.That(score.Unexpected, Is.EqualTo(new[] { "includedConsignmentItem[0]/transportDangerousGoods[0]/uNDGID = 1263", "numberOfPackages = 22" }), "un valore diverso da quello atteso è una proposta non attesa");
        Assert.That(score.Passed, Is.False);
    }

    [Test]
    public void MappingGolden_NegativeCases_ExpectNoProposal()
    {
        //SUT
        MappingGolden golden = MappingGolden.Load(TestData.Directory);

        Assert.That(golden.Cases.Select(c => c.ShipmentCode), Is.Unique);
        Assert.That(golden.Cases.Count(c => c.Expected.Count == 0), Is.GreaterThanOrEqualTo(3), "spedizioni in cui l'ordine non contiene il dato che manca");
        Assert.That(golden.Cases.Select(c => "TO-" + c.ShipmentCode["SH-".Length..]), Is.SubsetOf(GoldenCmrs.Orders.Keys), "ogni spedizione nasce da un ordine del seed");
    }

    [Test]
    public void ModelPrices_CostIsTokensTimesListPrice_AndALocalModelCostsNothing()
    {
        //SETUP
        var prices = new ModelPrices(ModelPrices.Defaults);

        //SUT
        decimal? haiku = prices.Cost(ModelCandidate.Parse("anthropic:claude-haiku-4-5"), 2_000_000, 1_000_000);
        decimal? sonnet = prices.Cost(ModelCandidate.Parse("anthropic:claude-sonnet-5-5"), 500_000, 100_000);

        Assert.That((haiku, sonnet), Is.EqualTo((7.00m, 2.00m)));
        Assert.That(prices.Cost(ModelCandidate.Parse("ollama:qwen3.5:9b"), 5_000, 5_000), Is.EqualTo(0m));
        Assert.That(prices.Cost(ModelCandidate.Parse("anthropic:modello-senza-listino"), 5_000, 5_000), Is.Null);
        Assert.That(prices.Cost(ModelCandidate.Parse("anthropic:claude-haiku-4-5"), null, null), Is.Null, "senza token non c'è un costo da dichiarare");
    }

    [Test]
    public void Cli_ReadsCommandOptionsAndModels()
    {
        //SUT
        Cli cli = Cli.Parse(["eval", "extraction", "--provider", "anthropic", "--model", "claude-haiku-4-5", "--limit", "5", "--reuse", "--threshold", "0.75"]);
        Cli bench = Cli.Parse(["bench", "models", "--models", "ollama:qwen3.5:9b, anthropic:claude-sonnet-5-5"]);
        var fallback = new ModelCandidate(ModelProviders.Ollama, "qwen3.5:9b");

        Assert.That((cli.Command, cli.Int("limit"), cli.Has("reuse"), cli.Decimal("threshold")), Is.EqualTo(("eval extraction", 5, true, 0.75m)));
        Assert.That(cli.Model(fallback), Is.EqualTo(new ModelCandidate("anthropic", "claude-haiku-4-5")));
        Assert.That(Cli.Parse(["eval", "security"]).Model(fallback), Is.Null);
        Assert.That(Cli.Parse(["x", "--model", "ollama:qwen3.5:9b"]).Model(fallback), Is.EqualTo(fallback), "il nome di un modello Ollama contiene i due punti");
        Assert.That(bench.Models(ModelCandidate.Defaults).Select(m => m.ToString()), Is.EqualTo(new[] { "ollama:qwen3.5:9b", "anthropic:claude-sonnet-5-5" }));
        Assert.That(Cli.Parse(["bench", "models"]).Models(ModelCandidate.Defaults), Has.Count.EqualTo(3));
        Assert.That(() => ModelCandidate.Parse("azure:gpt"), Throws.ArgumentException);
        Assert.That(() => cli.Int("threshold"), Throws.ArgumentException);
        Assert.That(new ModelCandidate("ollama", "qwen3.5:9b").FileName, Is.EqualTo("qwen3.5-9b"));
    }

    [Test]
    public void ValidationEvaluation_GoldenSetAndSeedDefects_AreAllRecognized()
    {
        //SETUP
        EftiValidator validator = EftiValidator.CreateDefault();

        //SUT
        IReadOnlyList<ValidationCaseResult> cases = ValidationCommand.RunGolden(validator, Path.Combine(TestData.Directory, GoldenValidationSet.FileName));
        IReadOnlyList<SeedDefectResult> defects = ValidationCommand.RunSeedDefects(validator);
        string report = ValidationCommand.Render(validator, cases);

        Assert.That(cases.Where(c => !c.Passed).Select(c => c.Name), Is.Empty);
        Assert.That(defects.Select(d => d.Defect), Is.EquivalentTo(Enum.GetValues<Tms.Data.Seed.SeedDefect>()));
        Assert.That(defects, Has.All.Matches<SeedDefectResult>(d => d.Orders > 0 && d.Recognized == d.Orders));
        Assert.That(report, Does.Contain("## Golden set").And.Contain("## Difetti voluti del seed").And.Contain($"{cases.Count} su {cases.Count} (100,0 %)"));
    }
}
