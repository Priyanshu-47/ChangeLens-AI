namespace ChangeLens.Application.Common;

public static class RepositoryUrlValidator
{
    /// <summary>
    /// Accepts https/http URLs, git@host:path SSH URLs, file:// URLs, and simple
    /// relative local paths (for demo repositories checked out on disk). Rejects
    /// absolute Windows paths, other schemes, whitespace, and path-traversal-ish input.
    /// </summary>
    public static bool IsValid(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500)
        {
            return false;
        }

        var trimmed = url.Trim();

        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                   && uri.Host.Length > 0;
        }

        if (trimmed.StartsWith("git@", StringComparison.Ordinal))
        {
            return trimmed.Contains(':') && !trimmed.Contains(' ');
        }

        if (trimmed.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
        {
            return Uri.TryCreate(trimmed, UriKind.Absolute, out _);
        }

        // Relative local path (demo mode). Reject absolute paths, drive letters,
        // traversal sequences, other URI schemes, and anything containing whitespace.
        if (trimmed.StartsWith('/') || trimmed.Contains('\\') || trimmed.Contains(' ') ||
            trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.Contains("://", StringComparison.Ordinal) ||
            trimmed.Length >= 2 && trimmed[1] == ':')
        {
            return false;
        }

        return true;
    }
}
