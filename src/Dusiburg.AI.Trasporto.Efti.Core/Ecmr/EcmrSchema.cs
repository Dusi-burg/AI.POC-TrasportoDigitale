using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Ecmr;

/// <summary>
/// Lo schema XSD dell'eCMR UN/CEFACT (Fase 8b), caricato e compilato dai file copiati accanto all'assembly. È la fonte dei nomi
/// degli elementi, del loro ordine, dei namespace e delle liste di codici: nulla di tutto questo è scritto a mano nel codice.
/// </summary>
public sealed class EcmrSchema
{
    public const string RootSchemaFile = "eCMR_100pD25A.xsd";

    private static readonly Lazy<EcmrSchema> LazyDefault = new(() => Load(DefaultDirectory));

    private EcmrSchema(string schemaPath, XmlSchemaSet schemas, XmlSchema rootSchema, XmlSchemaElement rootElement)
    {
        SchemaPath = schemaPath;
        Schemas = schemas;
        Version = rootSchema.Version ?? throw new InvalidOperationException($"Lo schema {schemaPath} non dichiara la versione.");
        TargetNamespace = rootSchema.TargetNamespace ?? throw new InvalidOperationException($"Lo schema {schemaPath} non ha un namespace.");
        RootElement = rootElement;
    }

    /// <summary>Gli schemi copiati accanto all'assembly da <c>data/ecmr-schema/uncefact</c>.</summary>
    public static string DefaultDirectory => Path.Combine(AppContext.BaseDirectory, "ecmr-schema", "uncefact");

    public static EcmrSchema Default => LazyDefault.Value;

    public string SchemaPath { get; }

    /// <summary>Schema radice e schemi importati (tipi e liste di codici), già compilati.</summary>
    public XmlSchemaSet Schemas { get; }

    /// <summary>Attributo <c>version</c> dello schema radice (es. <c>100.D25A</c>).</summary>
    public string Version { get; }

    public string TargetNamespace { get; }

    /// <summary>L'elemento <c>eCMR</c>.</summary>
    public XmlSchemaElement RootElement { get; }

    public int SchemaCount => Schemas.Count;

    public static EcmrSchema Load(string directory)
    {
        string path = Path.Combine(directory, RootSchemaFile);

        // Lo schema radice importa tipi e liste di codici con percorsi relativi: si carica dal file, non da una risorsa.
        var schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        XmlSchema rootSchema = schemas.Add(null, path) ?? throw new InvalidOperationException($"Schema eCMR non leggibile: {path}.");

        schemas.Compile();

        XmlSchemaElement rootElement = schemas.GlobalElements.Values
            .OfType<XmlSchemaElement>()
            .Single(element => element.QualifiedName.Namespace == rootSchema.TargetNamespace);

        return new EcmrSchema(path, schemas, rootSchema, rootElement);
    }

    /// <summary>Errori di conformità dell'XML allo schema; vuoto se il documento è conforme.</summary>
    public IReadOnlyList<string> Validate(XDocument document)
    {
        List<string> errors = [];

        document.Validate(Schemas, (_, e) => errors.Add(e.Message));

        return errors;
    }
}
