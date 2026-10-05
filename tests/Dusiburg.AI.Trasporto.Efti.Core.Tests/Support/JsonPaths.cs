using System.Text.Json.Nodes;
using Dusiburg.AI.Trasporto.Efti.Core.Dataset;

namespace Dusiburg.AI.Trasporto.Efti.Core.Tests.Support;

internal static class JsonPaths
{
    /// <summary>Percorsi dei valori di un dataset eFTI in JSON, senza indici: un oggetto <c>{ value, attributo }</c> conta come valore.</summary>
    public static HashSet<string> Leaves(JsonNode node)
    {
        HashSet<string> paths = [];

        Collect(node, string.Empty, paths);

        return paths;
    }

    public static void Collect(JsonNode node, string path, HashSet<string> paths)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (JsonNode? item in array)
                {
                    Collect(item!, path, paths);
                }

                break;

            case JsonObject json when !json.ContainsKey(EftiXmlExporter.ValueProperty):
                foreach ((string name, JsonNode? child) in json)
                {
                    Collect(child!, path.Length == 0 ? name : $"{path}/{name}", paths);
                }

                break;

            default:
                paths.Add(path);
                break;
        }
    }
}
