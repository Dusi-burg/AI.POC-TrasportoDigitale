using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Dusiburg.AI.Trasporto.Shared.Serialization;

/// <summary>JSON dei contratti HTTP: camelCase ed enum come stringhe, uguale nelle API e nei loro client.</summary>
public static class TrasportoJson
{
    // Il resolver è esplicito: chi riceve queste opzioni (per esempio l'output strutturato di un modello) le rende di sola
    // lettura prima di usarle, e senza un resolver non potrebbe.
    public static readonly JsonSerializerOptions Options =
        Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() });

    /// <summary>Applica le stesse regole alle opzioni dell'host (<c>ConfigureHttpJsonOptions</c>).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
