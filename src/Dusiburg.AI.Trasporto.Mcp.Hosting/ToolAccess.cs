using System.Reflection;
using Dusiburg.AI.Trasporto.Shared.Security;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Dusiburg.AI.Trasporto.Mcp.Hosting;

/// <summary>
/// L'operazione della matrice dei ruoli a cui corrisponde un tool: un tool, una operazione. Chi può eseguire l'operazione vede
/// il tool.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class ToolOperationAttribute(Operation operation) : Attribute
{
    public Operation Operation { get; } = operation;
}

/// <summary>Tool → operazione, letto dagli attributi della classe dei tool. Un tool senza operazione non fa partire il server.</summary>
public sealed class ToolOperationMap
{
    private readonly Dictionary<string, Operation> _operations;

    private ToolOperationMap(Dictionary<string, Operation> operations) => _operations = operations;

    public IReadOnlyDictionary<string, Operation> Operations => _operations;

    public static ToolOperationMap From(Type toolType)
    {
        Dictionary<string, Operation> operations = [];

        foreach (MethodInfo method in toolType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is not { } tool)
            {
                continue;
            }

            ToolOperationAttribute operation = method.GetCustomAttribute<ToolOperationAttribute>()
                ?? throw new InvalidOperationException($"Il tool {toolType.Name}.{method.Name} non dichiara la propria operazione ([ToolOperation]).");

            operations.Add(tool.Name ?? method.Name, operation.Operation);
        }

        return new ToolOperationMap(operations);
    }
}

/// <summary>
/// Che cosa può fare chi sta chiamando. Il server MCP non decide gli accessi: lo chiede al servizio che possiede la matrice
/// dei ruoli, con la chiave di chi chiama.
/// </summary>
public interface ICallerPermissions
{
    Task<IReadOnlySet<Operation>> AllowedOperationsAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Filtro su <c>tools/list</c>: un ruolo vede solo i tool la cui operazione gli è consentita. È ciò che un agente può provare a
/// usare; il rifiuto di una chiamata fuori ruolo lo decide e lo registra comunque l'API a valle.
/// </summary>
internal static class ToolListFilter
{
    public static McpRequestFilter<ListToolsRequestParams, ListToolsResult> Create() =>
        next => async (context, cancellationToken) =>
        {
            ListToolsResult result = await next(context, cancellationToken);

            IServiceProvider services = context.Services ?? throw new InvalidOperationException("Richiesta MCP senza servizi.");
            ToolOperationMap map = services.GetRequiredService<ToolOperationMap>();
            IReadOnlySet<Operation> allowed = await services.GetRequiredService<ICallerPermissions>().AllowedOperationsAsync(cancellationToken);

            // Un tool che non è nella mappa non si mostra a nessuno: ciò che non è dichiarato è negato.
            result.Tools = [.. result.Tools.Where(tool => map.Operations.TryGetValue(tool.Name, out Operation operation) && allowed.Contains(operation))];

            return result;
        };
}
