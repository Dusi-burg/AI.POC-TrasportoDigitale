using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>Sottoinsiemi del dataset che il POC mostra all'autorità (D6).</summary>
public static class EftiSubsets
{
    /// <summary>Dati generali del trasporto: soggetti, luoghi, date, natura e massa della merce, targhe.</summary>
    public const string Eu03 = "EU03";

    /// <summary>Merci pericolose (ADR).</summary>
    public const string Eu05a = "EU05a";

    public static readonly IReadOnlyList<string> Authority = [Eu03, Eu05a];

    public static bool IsAuthoritySubset(string? subsetId) => subsetId is not null && Authority.Contains(subsetId);
}

/// <summary>
/// Proiezioni del dataset ricavate dallo schema (D6, D7): nessun elenco di campi scritto a mano. Una proiezione non aggiunge e
/// non trasforma: tiene o toglie elementi del JSON, che continua a usare i nomi dello schema.
/// </summary>
public sealed class EftiSubsetProjector(EftiSchemaCatalog common, EftiSchemaCatalog identifier)
{
    public static EftiSubsetProjector CreateDefault() => new(EftiSchemaCatalog.Default, EftiSchemaCatalog.Identifier);

    /// <summary>
    /// Il sottoinsieme richiesto: restano gli elementi che lo schema annota con quel sottoinsieme, a ogni livello, come fa
    /// <c>SubsetUtil.dropNodesNotInSubsets</c> degli strumenti di riferimento. Un contenitore rimasto vuoto si toglie.
    /// </summary>
    public JsonObject ProjectSubset(string datasetJson, string subsetId) =>
        Project(Parse(datasetJson), common, common.RootTypeName, element => element.IsInSubset(subsetId));

    /// <summary>
    /// Il sottoinsieme degli identificatori da registrare sul gate: restano gli elementi che esistono in
    /// <c>consignment-identifier.xsd</c>, che usa gli stessi nomi dello schema comune.
    /// </summary>
    public JsonObject ProjectIdentifiers(string datasetJson) =>
        Project(Parse(datasetJson), identifier, identifier.RootTypeName, _ => true);

    private static JsonObject Parse(string datasetJson) =>
        JsonNode.Parse(datasetJson) as JsonObject ?? throw new EftiDatasetException("Dataset eFTI: atteso un oggetto JSON.");

    /// <summary>Tiene le proprietà che lo schema dato conosce e che superano il filtro; scende nei contenitori.</summary>
    private static JsonObject Project(JsonObject source, EftiSchemaCatalog catalog, string typeName, Func<SchemaElement, bool> keep)
    {
        var projected = new JsonObject();

        foreach (SchemaElement element in catalog.ElementsOf(typeName).Where(keep))
        {
            if (source[element.Name] is { } value && ProjectValue(value, catalog, element, keep) is { } kept)
            {
                projected[element.Name] = kept;
            }
        }

        return projected;
    }

    private static JsonNode? ProjectValue(JsonNode value, EftiSchemaCatalog catalog, SchemaElement element, Func<SchemaElement, bool> keep)
    {
        if (!element.IsComplex)
        {
            return value.DeepClone();
        }

        if (value is JsonArray array)
        {
            JsonNode[] items = [.. array.OfType<JsonObject>()
                .Select(item => Project(item, catalog, element.ComplexTypeName, keep))
                .Where(item => item.Count > 0)];

            return items.Length == 0 ? null : new JsonArray(items);
        }

        JsonObject projected = Project(value.AsObject(), catalog, element.ComplexTypeName, keep);

        return projected.Count == 0 ? null : projected;
    }
}
