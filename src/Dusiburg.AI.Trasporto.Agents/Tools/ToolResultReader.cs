using System.Text.Json;
using Dusiburg.AI.Trasporto.Shared.Errors;
using Dusiburg.AI.Trasporto.Shared.Serialization;

namespace Dusiburg.AI.Trasporto.Agents.Tools;

public readonly record struct ToolResult(bool Succeeded, JsonElement Data, string? ErrorCode, string? ErrorMessage);

/// <summary>
/// Interpreta il valore restituito da un tool: un risultato MCP (<c>structuredContent</c>, <c>content</c>, <c>isError</c>)
/// oppure direttamente l'oggetto del risultato; l'involucro <c>{ error: { code, message } }</c> è sempre un errore.
/// </summary>
public static class ToolResultReader
{
    public static ToolResult Read(object? value)
    {
        JsonElement element = value switch
        {
            JsonElement json => json,
            null => default,
            string text => TryParse(text) ?? JsonSerializer.SerializeToElement(text),
            _ => JsonSerializer.SerializeToElement(value, TrasportoJson.Options)
        };

        if (element.ValueKind != JsonValueKind.Object)
        {
            return new ToolResult(true, element, null, null);
        }

        if (TryReadError(element, out string? code, out string? message))
        {
            return new ToolResult(false, element, code, message);
        }

        bool isError = element.TryGetProperty("isError", out JsonElement flag) && flag.ValueKind == JsonValueKind.True;

        if (!isError && element.TryGetProperty("structuredContent", out JsonElement structured) && structured.ValueKind == JsonValueKind.Object)
        {
            return new ToolResult(true, structured, null, null);
        }

        if (element.TryGetProperty("content", out JsonElement content) && content.ValueKind == JsonValueKind.Array)
        {
            string text = string.Concat(content.EnumerateArray()
                .Where(block => block.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
                .Select(block => block.TryGetProperty("text", out JsonElement t) ? t.GetString() : null));

            JsonElement? parsed = TryParse(text);

            if (parsed is { ValueKind: JsonValueKind.Object } payload && TryReadError(payload, out code, out message))
            {
                return new ToolResult(false, payload, code, message);
            }

            return isError
                ? new ToolResult(false, element, ErrorCodes.Internal, text)
                : new ToolResult(true, parsed ?? element, null, null);
        }

        return isError
            ? new ToolResult(false, element, ErrorCodes.Internal, null)
            : new ToolResult(true, element, null, null);
    }

    private static bool TryReadError(JsonElement element, out string? code, out string? message)
    {
        code = null;
        message = null;

        if (!element.TryGetProperty("error", out JsonElement error) || error.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        code = error.TryGetProperty("code", out JsonElement c) ? c.GetString() : ErrorCodes.Internal;
        message = error.TryGetProperty("message", out JsonElement m) ? m.GetString() : null;

        return true;
    }

    private static JsonElement? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);

            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
