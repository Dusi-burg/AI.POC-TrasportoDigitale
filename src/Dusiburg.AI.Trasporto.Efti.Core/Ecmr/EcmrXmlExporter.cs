using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Ecmr;

/// <summary>Il JSON dell'eCMR non corrisponde allo schema: nome di elemento sconosciuto o forma sbagliata.</summary>
public sealed class EcmrDocumentException(string message) : Exception(message);

/// <summary>
/// JSON dell'eCMR → XML UN/CEFACT, guidato dallo schema compilato: ordine degli elementi, namespace e distinzione fra
/// contenitori e valori vengono da <see cref="EcmrSchema"/>, non da codice scritto campo per campo. Un nome che lo schema non
/// conosce è un errore: così il JSON salvato non può divergere dai nomi dello schema.
/// </summary>
/// <remarks>
/// Forma del JSON: un elemento ripetibile è un array; un valore con attributo (unità, valuta, formato) è un oggetto
/// <c>{ "value": …, "&lt;attributo&gt;": … }</c>; data e indicatore, che nello schema sono una scelta fra due elementi, sono un
/// oggetto con l'elemento scelto (<c>{ "DateTime": "…" }</c>, <c>{ "Indicator": true }</c>).
/// </remarks>
public sealed class EcmrXmlExporter(EcmrSchema schema)
{
    /// <summary>Nome JSON del contenuto di un valore con attributo.</summary>
    public const string ValueProperty = "value";

    /// <summary>Prefissi dei namespace, come negli schemi pubblicati: rendono l'XML leggibile.</summary>
    private static readonly (string Prefix, string Namespace)[] Prefixes =
    [
        ("rsm", "urn:un:unece:uncefact:data:standard:eCMR:100"),
        ("ram", "urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100"),
        ("udt", "urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100"),
        ("qdt", "urn:un:unece:uncefact:data:standard:QualifiedDataType:100")
    ];

    public XDocument Export(string documentJson)
    {
        JsonNode root = JsonNode.Parse(documentJson) ?? throw new EcmrDocumentException("Documento eCMR vuoto.");
        XElement element = Element(schema.RootElement, root, schema.RootElement.Name!);

        foreach ((string prefix, string ns) in Prefixes)
        {
            element.SetAttributeValue(XNamespace.Xmlns + prefix, ns);
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", null), element);
    }

    private XElement Element(XmlSchemaElement declaration, JsonNode value, string path)
    {
        XName name = XName.Get(declaration.QualifiedName.Name, declaration.QualifiedName.Namespace);

        return declaration.ElementSchemaType is XmlSchemaComplexType { ContentType: XmlSchemaContentType.ElementOnly } type
            ? Container(name, type, value, path)
            : Leaf(name, declaration.ElementSchemaType as XmlSchemaComplexType, value, path);
    }

    private XElement Container(XName name, XmlSchemaComplexType type, JsonNode value, string path)
    {
        if (value is not JsonObject json)
        {
            throw new EcmrDocumentException($"{path}: atteso un oggetto, trovato {value.GetValueKind()}.");
        }

        // Sequenza o scelta: in entrambi i casi gli elementi figli sono quelli elencati, nell'ordine dello schema.
        XmlSchemaElement[] children = type.ContentTypeParticle is XmlSchemaGroupBase group ? [.. group.Items.OfType<XmlSchemaElement>()] : [];
        string[] unknown = [.. json.Select(p => p.Key).Where(key => children.All(c => c.Name != key))];

        if (unknown.Length > 0)
        {
            throw new EcmrDocumentException($"{path}: elementi assenti dallo schema eCMR {schema.Version}: {string.Join(", ", unknown)}.");
        }

        var element = new XElement(name);

        foreach (XmlSchemaElement child in children)
        {
            if (json[child.Name!] is not { } childValue)
            {
                continue;
            }

            string childPath = $"{path}/{child.Name}";

            if (childValue is JsonArray array)
            {
                if (child.MaxOccurs <= 1 && array.Count > 1)
                {
                    throw new EcmrDocumentException($"{childPath}: lo schema ammette un solo valore, trovati {array.Count}.");
                }

                element.Add(array.Select((item, index) =>
                    Element(child, item ?? throw new EcmrDocumentException($"{childPath}[{index}]: valore null."), $"{childPath}[{index}]")));
            }
            else
            {
                element.Add(Element(child, childValue, childPath));
            }
        }

        return element;
    }

    /// <summary>Valore semplice, oppure oggetto <c>{ value, attributo… }</c> per i tipi con unità, valuta, formato o agenzia.</summary>
    private XElement Leaf(XName name, XmlSchemaComplexType? type, JsonNode value, string path)
    {
        if (value is not JsonObject json)
        {
            return new XElement(name, Text(value, path));
        }

        JsonNode content = json[ValueProperty] ?? throw new EcmrDocumentException($"{path}: manca '{ValueProperty}'.");
        var element = new XElement(name, Text(content, path));

        foreach ((string attribute, JsonNode? attributeValue) in json.Where(p => p.Key != ValueProperty))
        {
            if (type is null || !type.AttributeUses.Names.Cast<System.Xml.XmlQualifiedName>().Any(a => a.Name == attribute))
            {
                throw new EcmrDocumentException($"{path}: attributo '{attribute}' assente dallo schema eCMR {schema.Version}.");
            }

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
        _ => throw new EcmrDocumentException($"{path}: atteso un valore semplice, trovato {value.GetValueKind()}.")
    };
}
