using System.Text.Json;

namespace ChangeLens.Application.Tools;

/// <summary>
/// Defensive argument extraction for tools. Arguments are AI-supplied (untrusted):
/// wrong types, invalid UUIDs, empty identifiers, and path-ish strings are rejected
/// as INVALID_ARGUMENT before any execution (docs/agent-tools.md §8/§10–11).
/// </summary>
internal static class ToolArguments
{
    public static bool TryGuid(JsonElement args, string key, out Guid value, out string? error)
    {
        value = Guid.Empty;
        error = null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind is JsonValueKind.Null)
        {
            error = $"Missing required argument '{key}'.";
            return false;
        }

        if (prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        error = $"Argument '{key}' must be a valid UUID.";
        return false;
    }

    public static bool TryString(
        JsonElement args, string key, int maxLength, out string value, out string? error)
    {
        value = string.Empty;
        error = null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind is JsonValueKind.Null)
        {
            error = $"Missing required argument '{key}'.";
            return false;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString()?.Trim() ?? string.Empty;
            if (value.Length == 0)
            {
                error = $"Argument '{key}' must not be empty.";
                return false;
            }

            if (value.Length > maxLength)
            {
                error = $"Argument '{key}' exceeds the maximum length of {maxLength}.";
                return false;
            }

            return true;
        }

        error = $"Argument '{key}' must be a string.";
        return false;
    }

    public static bool TryInt(
        JsonElement args, string key, int min, int max, int defaultValue, out int value, out string? error)
    {
        value = defaultValue;
        error = null;

        if (!args.TryGetProperty(key, out var prop) || prop.ValueKind is JsonValueKind.Null)
        {
            return true; // optional — default applies
        }

        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var parsed))
        {
            if (parsed < min || parsed > max)
            {
                error = $"Argument '{key}' must be between {min} and {max}.";
                return false;
            }

            value = parsed;
            return true;
        }

        error = $"Argument '{key}' must be an integer.";
        return false;
    }

    /// <summary>
    /// Identifier safety for symbol/path-like arguments (brief §10/§11): rejects
    /// traversal, drive letters, URI schemes, and shell metacharacters. Symbols may
    /// contain spaces (parameter lists), so a focused denylist is used.
    /// </summary>
    public static bool IsSafeIdentifier(string value)
    {
        if (value.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (value.IndexOfAny(['/', '\\', ':', ';', '|', '$', '`', '"', '\'', '<', '>', '\n', '\r']) >= 0)
        {
            return false;
        }

        if (value.StartsWith("file", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    /// <summary>Truncates an argument summary for the trace (identifiers only, never secrets).</summary>
    public static string Summarize(JsonElement args, int maxChars = 200)
    {
        var json = args.GetRawText();
        return json.Length <= maxChars ? json : json[..maxChars];
    }
}
