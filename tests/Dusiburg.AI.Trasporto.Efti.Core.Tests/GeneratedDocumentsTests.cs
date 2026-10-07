using System.Text.Json;
using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;
using Dusiburg.AI.Trasporto.Efti.Core.Documents;
using Dusiburg.AI.Trasporto.Efti.Core.Ecmr;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;
using Dusiburg.AI.Trasporto.Efti.Core.Validation;
using Dusiburg.AI.Trasporto.Shared.Security;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests;

/// <summary>
/// Allineamento fra codice e documenti generati (P9): i file di <c>docs/</c> sono quelli che il codice produce oggi, e la
/// copertura dichiarata è quella che il mapper scrive davvero.
/// </summary>
public class GeneratedDocumentsTests
{
    private const string Regenerate = "rigenerare con: dotnet run --project tools/Dusiburg.AI.Trasporto.Eval -- docs";

    private static readonly EftiSchemaCatalog Catalog = EftiSchemaCatalog.Default;

    [Test]
    public void ValidationRulesDocument_InRepository_MatchesTheRules()
    {
        //SETUP
        string onDisk = ReadDocument(GeneratedDocuments.ValidationRulesFile);

        //SUT
        string rendered = GeneratedDocuments.RenderValidationRules();

        Assert.That(onDisk, Is.EqualTo(rendered), $"docs/{GeneratedDocuments.ValidationRulesFile} non è allineato alle regole: {Regenerate}");
        Assert.That(ValidationRules.All.Select(r => $"`{r.Code}`"), Is.All.Matches<string>(rendered.Contains));
    }

    [Test]
    public void CoverageDocument_InRepository_MatchesSchemaAndCoverage()
    {
        //SETUP
        string onDisk = ReadDocument(GeneratedDocuments.CoverageFile);

        //SUT
        string rendered = GeneratedDocuments.RenderCoverage(Catalog);

        Assert.That(onDisk, Is.EqualTo(rendered), $"docs/{GeneratedDocuments.CoverageFile} non è allineato a schema e copertura: {Regenerate}");
        Assert.That(rendered, Does.Contain("versione **0.8**"));
    }

    [Test]
    public void RolesDocument_InRepository_MatchesTheMatrix()
    {
        //SETUP
        string onDisk = ReadDocument(GeneratedDocuments.RolesFile);

        //SUT
        string rendered = GeneratedDocuments.RenderRoles();

        Assert.That(onDisk, Is.EqualTo(rendered), $"docs/{GeneratedDocuments.RolesFile} non è allineato alla matrice dei ruoli: {Regenerate}");
        Assert.That(Enum.GetNames<Operation>().Concat(Enum.GetNames<Role>()).Select(name => $"`{name}`"), Is.All.Matches<string>(rendered.Contains));
    }

    [Test]
    public void EcmrRulesDocument_InRepository_MatchesTheRules()
    {
        //SETUP
        string onDisk = ReadDocument(GeneratedDocuments.EcmrRulesFile);

        //SUT
        string rendered = GeneratedDocuments.RenderEcmrRules(EcmrSchema.Default);

        Assert.That(onDisk, Is.EqualTo(rendered), $"docs/{GeneratedDocuments.EcmrRulesFile} non è allineato alle regole: {Regenerate}");
        Assert.That(EcmrRules.All.Select(r => $"`{r.Code}`"), Is.All.Matches<string>(rendered.Contains));
    }

    [Test]
    public void Coverage_EveryDeclaredElement_ExistsInTheSchemaAsDataElement()
    {
        //SUT
        IReadOnlyList<CoveredElement> elements = DatasetCoverage.Elements;

        Assert.That(elements.Select(e => e.Path), Is.Unique);
        Assert.That(elements.Select(e => (e.Path, Element: Catalog.Find(e.Path))),
            Is.All.Matches<(string Path, SchemaElement? Element)>(e => e.Element is { IsComplex: false }));
    }

    [Test]
    public void Coverage_DeclaresExactlyWhatTheMapperWrites()
    {
        //SETUP
        HashSet<string> written = [];

        foreach (string json in SeedShipments.All().Select(s => EftiDatasetJson.Serialize(EftiDatasetMapper.Map(s))))
        {
            CollectLeafPaths(JsonNode.Parse(json)!, string.Empty, written);
        }

        //SUT
        string[] declared = [.. DatasetCoverage.Elements.Select(e => e.Path)];

        // Il seed è abbastanza vario da far scrivere al mapper tutti gli elementi dichiarati: se uno manca, o è morto o va tolto.
        Assert.That(written, Is.EquivalentTo(declared));
    }

    [Test]
    public void UncoveredSubsetElements_AreDeclared_AndNoCoveredElementIsAmongThem()
    {
        //SUT
        IReadOnlyList<(string Path, SchemaElement Element)> uncovered = GeneratedDocuments.UncoveredSubsetElements(Catalog);

        string[] paths = [.. uncovered.Select(u => u.Path)];
        Assert.That(paths, Does.Contain("consignor/id").And.Contain("includedConsignmentItem/transportDangerousGoods/tunnelRestrictionCode"));
        Assert.That(paths, Has.None.EqualTo("mainCarriageTransportMovement/usedTransportMeans/typeCode"), "tipo di mezzo: coperto dalla Fase 4");
        Assert.That(paths.Intersect(DatasetCoverage.Elements.Select(e => e.Path)), Is.Empty);
        Assert.That(uncovered.Select(u => u.Element), Is.All.Matches<SchemaElement>(e => GeneratedDocuments.AuthoritySubsets.Any(e.IsInSubset)));
    }

    /// <summary>Percorsi dei valori del dataset, senza indici: un oggetto <c>{ value, attributo }</c> conta come valore.</summary>
    private static void CollectLeafPaths(JsonNode node, string path, HashSet<string> paths)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    CollectLeafPaths(item!, path, paths);
                }

                break;

            case JsonObject json when !json.ContainsKey(EftiXmlExporter.ValueProperty):
                foreach ((string name, JsonNode? child) in json)
                {
                    CollectLeafPaths(child!, path.Length == 0 ? name : $"{path}/{name}", paths);
                }

                break;

            default:
                paths.Add(path);
                break;
        }
    }

    private static string ReadDocument(string fileName) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", fileName)).ReplaceLineEndings("\n");

    private static string RepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (directory.EnumerateFiles("*.slnx").Any())
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Radice del repository non trovata.");
    }
}
