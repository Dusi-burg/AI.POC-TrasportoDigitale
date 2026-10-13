using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Dusiburg.AI.Trasporto.Efti.Core.Schema;

namespace Dusiburg.AI.Trasporto.Efti.Core.Dataset;

/// <summary>
/// Conformità dell'XML esportato allo schema XSD eFTI. Lo schema dichiara facoltativo ogni elemento: qui si controllano
/// struttura, ordine, tipi, lunghezze e liste di codici; l'obbligatorietà è compito delle regole dichiarative.
/// </summary>
public sealed class EftiXmlValidator
{
    private readonly XmlSchemaSet _schemas;

    public EftiXmlValidator(EftiSchemaCatalog catalog)
    {
        // Lo schema comune importa i tipi e le liste di codici con percorsi relativi: si carica dal file, non da una risorsa.
        _schemas = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        _schemas.Add(catalog.TargetNamespace, catalog.SchemaPath);
        _schemas.Compile();
    }

    /// <summary>Errori di conformità; vuoto se il documento è conforme.</summary>
    public IReadOnlyList<string> Validate(XDocument document)
    {
        List<string> errors = [];

        document.Validate(_schemas, (_, e) => errors.Add(e.Message));

        return errors;
    }
}
