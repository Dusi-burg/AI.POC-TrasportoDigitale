namespace Dusiburg.AI.Trasporto.Shared.Telemetry;

public static class TrasportoTelemetry
{
    /// <summary>Filtro usato da ServiceDefaults per raccogliere tutte le sorgenti del POC.</summary>
    public const string SourceWildcard = "Dusiburg.AI.Trasporto.*";

    public static class Sources
    {
        public const string Chat = "Dusiburg.AI.Trasporto.Ai.Chat";
        public const string Embedding = "Dusiburg.AI.Trasporto.Ai.Embedding";
        public const string McpEfti = "Dusiburg.AI.Trasporto.Mcp.Efti";
    }
}
