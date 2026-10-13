using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>Il JSON del dataset non corrisponde allo schema: nome di elemento sconosciuto o forma sbagliata.</summary>
public sealed class EftiDatasetException(string message) : Exception(message);

/// <summary>
/// JSON del dataset → XML eFTI, guidato dallo schema: l'ordine degli elementi e la distinzione fra contenitori e valori
/// vengono dal <see cref="EftiSchemaCatalog"/>, non da codice scritto campo per campo. Un nome che lo schema non conosce è un
/// errore: così il JSON salvato non può divergere dai nomi dello schema.
/// </summary>
public sealed class EftiXmlExporter(EftiSchemaCatalog catalog)
{
    /// <summary>Nome JSON del contenuto di un valore con attributo (<c>{ "value": 12.5, "unitId": "KGM" }</c>).</summary>
    public const string ValueProperty = "value";

    public XDocument Export(EftiConsignment consignment) => Export(EftiDatasetJson.Serialize(consignment));

    public XDocument Export(string datasetJson)
    {
        JsonNode root = JsonNode.Parse(datasetJson) ?? throw new EftiDatasetException("Dataset eFTI vuoto.");
        XNamespace ns = catalog.TargetNamespace;

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            ComplexElement(ns + catalog.RootElementName, catalog.RootTypeName, root, catalog.RootElementName));
    }

    private XElement ComplexElement(XName name, string typeName, JsonNode node, string path)
    {
        if (node is not JsonObject json)
        {
            throw new EftiDatasetException($"{path}: atteso un oggetto, trovato {node.GetValueKind()}.");
        }

        IReadOnlyList<SchemaElement> schemaElements = catalog.ElementsOf(typeName);
        string[] unknown = [.. json.Select(p => p.Key).Where(key => schemaElements.All(e => e.Name != key))];

        if (unknown.Length > 0)
        {
            throw new EftiDatasetException($"{path}: elementi assenti dallo schema eFTI {catalog.Version}: {string.Join(", ", unknown)}.");
        }

        var element = new XElement(name);

        // L'XSD impone una sequenza: si segue l'ordine dello schema, non quello del JSON.
        foreach (SchemaElement schemaElement in schemaElements)
        {
            if (json[schemaElement.Name] is { } value)
            {
                element.Add(ChildElements(name.Namespace + schemaElement.Name, schemaElement, value, $"{path}/{schemaElement.Name}"));
            }
        }

        return element;
    }

    private IEnumerable<XElement> ChildElements(XName name, SchemaElement schemaElement, JsonNode value, string path)
    {
        if (value is JsonArray array)
        {
            if (!schemaElement.IsRepeatable && array.Count > 1)
            {
                throw new EftiDatasetException($"{path}: lo schema ammette un solo valore, trovati {array.Count}.");
            }

            return [.. array.Select((item, index) => SingleElement(name, schemaElement, item ?? throw new EftiDatasetException($"{path}[{index}]: valore null."), $"{path}[{index}]"))];
        }

        return [SingleElement(name, schemaElement, value, path)];
    }

    private XElement SingleElement(XName name, SchemaElement schemaElement, JsonNode value, string path) =>
        schemaElement.IsComplex
            ? ComplexElement(name, schemaElement.ComplexTypeName, value, path)
            : SimpleElement(name, value, path);

    /// <summary>Valore semplice, oppure oggetto <c>{ value, attributo… }</c> per i tipi con unità, valuta o formato.</summary>
    private static XElement SimpleElement(XName name, JsonNode value, string path)
    {
        if (value is not JsonObject json)
        {
            return new XElement(name, Text(value, path));
        }

        JsonNode content = json[ValueProperty] ?? throw new EftiDatasetException($"{path}: manca '{ValueProperty}'.");
        var element = new XElement(name, Text(content, path));

        foreach ((string attribute, JsonNode? attributeValue) in json.Where(p => p.Key != ValueProperty))
        {
            if (attributeValue is not null)
            {
                element.SetAttributeValue(attribute, Text(attributeValue, $"{path}/@{attribute}"));
            }
        }

        return element;
    }

    private static string Text(JsonNode value, string path) => value.GetValueKind() switch
    {
        JsonValueKind.String => value.GetValue<string>(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        // Il testo JSON di un numero è già in formato invariante, che è quello di xsd:decimal.
        JsonValueKind.Number => value.ToJsonString(),
        _ => throw new EftiDatasetException($"{path}: atteso un valore semplice, trovato {value.GetValueKind()}.")
    };
}
