using System.Globalization;
using System.Xml.Linq;

namespace Dusiburg.AI.Trasporto.Efti.Core.Schema;

/// <summary>Sottoinsieme a cui un elemento appartiene, con lo stato scritto nello schema (<c>M</c>, <c>C</c>, <c>O</c>, <c>SI</c> e combinazioni).</summary>
public sealed record SchemaSubset(string Id, string Status);

/// <summary>Un elemento dello schema eFTI con le sue annotazioni.</summary>
/// <param name="TypeName">Tipo con il prefisso dello schema (<c>efti:TradeParty</c>, <c>types:Amount</c>, <c>xsd:token</c>).</param>
/// <param name="MaxOccurs"><c>null</c> = senza limite.</param>
/// <param name="EftiId">Identificativo eFTI: <c>eFTInnn</c> per i dati, <c>ASBIEnnnn</c> per le associazioni.</param>
public sealed record SchemaElement(
    string Name,
    string TypeName,
    int MinOccurs,
    int? MaxOccurs,
    string? EftiId,
    string? Format,
    string? Definition,
    IReadOnlyList<SchemaSubset> Subsets)
{
    /// <summary>Il tipo è un tipo complesso dello schema comune: l'elemento contiene altri elementi.</summary>
    public bool IsComplex => TypeName.StartsWith(EftiSchemaCatalog.ComplexTypePrefix, StringComparison.Ordinal);

    public string ComplexTypeName => TypeName[EftiSchemaCatalog.ComplexTypePrefix.Length..];

    public bool IsRepeatable => MaxOccurs is null or > 1;

    /// <summary>
    /// Un elemento appartiene a un sottoinsieme se la sua annotazione lo elenca: è il criterio degli strumenti di riferimento
    /// (<c>SubsetUtil.dropNodesNotInSubsets</c>), che non guarda lo stato.
    /// </summary>
    public bool IsInSubset(string subsetId) => Subsets.Any(s => s.Id == subsetId);

    public string? StatusIn(string subsetId) => Subsets.FirstOrDefault(s => s.Id == subsetId)?.Status;
}

/// <summary>
/// Lettura dello schema XSD eFTI (D5): è la fonte di nomi, identificativi, formati, definizioni e sottoinsiemi. Nulla di tutto
/// questo è scritto a mano nel codice.
/// </summary>
public sealed class EftiSchemaCatalog
{
    public const string ComplexTypePrefix = "efti:";

    public const string CommonSchemaFile = "consignment-common.xsd";

    private const string CountryCodesFile = "ISO_ISOTwo-letterCountryCode_SecondEdition2006.xsd";

    private static readonly XNamespace Xsd = "http://www.w3.org/2001/XMLSchema";

    /// <summary>Sottoinsieme degli identificatori: ciò che la piattaforma registra sul gate.</summary>
    public const string IdentifierSchemaFile = "consignment-identifier.xsd";

    private static readonly Lazy<EftiSchemaCatalog> LazyDefault = new(() => Load(DefaultDirectory));

    private static readonly Lazy<EftiSchemaCatalog> LazyIdentifier = new(() => Load(DefaultDirectory, IdentifierSchemaFile));

    private readonly Dictionary<string, IReadOnlyList<SchemaElement>> _types;

    private EftiSchemaCatalog(
        string directory, string fileName, string version, string targetNamespace, string rootElementName, string rootTypeName,
        Dictionary<string, IReadOnlyList<SchemaElement>> types, IReadOnlySet<string> countryCodes)
    {
        Directory = directory;
        FileName = fileName;
        Version = version;
        TargetNamespace = targetNamespace;
        RootElementName = rootElementName;
        RootTypeName = rootTypeName;
        CountryCodes = countryCodes;
        _types = types;
    }

    /// <summary>Gli schemi copiati accanto all'assembly da <c>data/efti-schema/v0</c>.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "efti-schema", "v0");

    /// <summary>Lo schema comune: il dataset completo.</summary>
    public static EftiSchemaCatalog Default => LazyDefault.Value;

    /// <summary>Lo schema degli identificatori: stessi nomi e stessi tipi dello schema comune, ridotti a ciò che va sul gate.</summary>
    public static EftiSchemaCatalog Identifier => LazyIdentifier.Value;

    public string Directory { get; }

    public string FileName { get; }

    public string SchemaPath => Path.Combine(Directory, FileName);

    /// <summary>Attributo <c>version</c> dello schema (es. <c>0.8</c> per quello comune).</summary>
    public string Version { get; }

    public string TargetNamespace { get; }

    public string RootElementName { get; }

    public string RootTypeName { get; }

    /// <summary>Codici paese ISO 3166-1 alpha-2 ammessi dallo schema.</summary>
    public IReadOnlySet<string> CountryCodes { get; }

    public IEnumerable<string> ComplexTypeNames => _types.Keys;

    public static EftiSchemaCatalog Load(string directory, string fileName = CommonSchemaFile)
    {
        XDocument schema = XDocument.Load(Path.Combine(directory, fileName));
        XElement root = schema.Root ?? throw new InvalidDataException($"{fileName}: documento vuoto.");
        XElement rootElement = root.Elements(Xsd + "element").Single();

        Dictionary<string, IReadOnlyList<SchemaElement>> types = root
            .Elements(Xsd + "complexType")
            .ToDictionary(
                type => Required(type, "name"),
                type => (IReadOnlyList<SchemaElement>)[.. type.Descendants(Xsd + "element").Select(ReadElement)]);

        return new EftiSchemaCatalog(
            directory,
            fileName,
            Required(root, "version"),
            Required(root, "targetNamespace"),
            Required(rootElement, "name"),
            Required(rootElement, "type")[ComplexTypePrefix.Length..],
            types,
            ReadEnumeration(Path.Combine(directory, "codes", CountryCodesFile)));
    }

    /// <summary>Elementi di un tipo complesso, nell'ordine dello schema: è l'ordine obbligatorio nell'XML.</summary>
    public IReadOnlyList<SchemaElement> ElementsOf(string complexTypeName) =>
        _types.TryGetValue(complexTypeName, out IReadOnlyList<SchemaElement>? elements)
            ? elements
            : throw new KeyNotFoundException($"Tipo '{complexTypeName}' assente dallo schema eFTI {Version}.");

    /// <summary>Elemento raggiunto da un percorso relativo alla radice, es. <c>consignor/postalAddress/postcode</c>.</summary>
    public SchemaElement? Find(string path)
    {
        string typeName = RootTypeName;
        SchemaElement? element = null;

        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            // Dopo un elemento semplice non si può scendere oltre.
            if (element is { IsComplex: false })
            {
                return null;
            }

            element = ElementsOf(typeName).FirstOrDefault(e => e.Name == segment);

            if (element is null)
            {
                return null;
            }

            if (element.IsComplex)
            {
                typeName = element.ComplexTypeName;
            }
        }

        return element;
    }

    private static SchemaElement ReadElement(XElement element)
    {
        XElement? efti = element
            .Element(Xsd + "annotation")?
            .Element(Xsd + "appinfo")?
            .Elements()
            .FirstOrDefault(e => e.Name.LocalName == "efti");

        return new SchemaElement(
            Required(element, "name"),
            Required(element, "type"),
            ReadOccurs(element.Attribute("minOccurs")?.Value) ?? 1,
            element.Attribute("maxOccurs")?.Value == "unbounded" ? null : ReadOccurs(element.Attribute("maxOccurs")?.Value) ?? 1,
            NonEmpty(efti?.Attribute("id")?.Value),
            NonEmpty(efti?.Attribute("format")?.Value),
            NonEmpty(efti?.Attribute("definition")?.Value),
            [.. (efti?.Elements().Where(e => e.Name.LocalName == "subset") ?? [])
                .Select(s => new SchemaSubset(Required(s, "id"), s.Attribute("status")?.Value ?? string.Empty))]);
    }

    private static IReadOnlySet<string> ReadEnumeration(string path) =>
        XDocument.Load(path).Descendants(Xsd + "enumeration").Select(e => Required(e, "value")).ToHashSet(StringComparer.Ordinal);

    private static int? ReadOccurs(string? value) => value is null ? null : int.Parse(value, CultureInfo.InvariantCulture);

    private static string Required(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value ?? throw new InvalidDataException($"<{element.Name.LocalName}> senza attributo '{attribute}'.");

    private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
