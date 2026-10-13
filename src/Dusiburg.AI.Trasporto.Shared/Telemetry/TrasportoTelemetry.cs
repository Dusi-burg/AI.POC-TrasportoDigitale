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
        public const string Agents = "Dusiburg.AI.Trasporto.Agents";
    }

    /// <summary>Attributi dello span di una chiamata a un tool MCP.</summary>
    public static class Attributes
    {
        public const string AgentName = "agent.name";
        public const string ToolName = "tool.name";
        public const string ActorRole = "actor.role";
        public const string ToolOutcome = "tool.outcome";
    }
}
