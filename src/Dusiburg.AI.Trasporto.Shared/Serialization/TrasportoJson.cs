using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dusiburg.AI.Trasporto.Shared.Serialization;

/// <summary>JSON dei contratti HTTP: camelCase ed enum come stringhe, uguale nelle API e nei loro client.</summary>
public static class TrasportoJson
{
    public static readonly JsonSerializerOptions Options = Configure(new JsonSerializerOptions(JsonSerializerDefaults.Web));

    /// <summary>Applica le stesse regole alle opzioni dell'host (<c>ConfigureHttpJsonOptions</c>).</summary>
    public static JsonSerializerOptions Configure(JsonSerializerOptions options)
    {
        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}
