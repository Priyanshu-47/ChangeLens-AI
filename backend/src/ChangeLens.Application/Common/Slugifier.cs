using System.Text;
using System.Text.RegularExpressions;

namespace ChangeLens.Application.Common;

public static partial class Slugifier
{
    private const int MaxLength = 140;

    /// <summary>
    /// Produces a URL-safe slug from a project name ("My Auth Service!" → "my-auth-service").
    /// Non-ASCII characters are transliterated to their closest ASCII forms where possible,
    /// otherwise stripped; an empty result falls back to "project".
    /// </summary>
    public static string Slugify(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new Exceptions.ValidationException("Name is required.");
        }

        var normalized = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (char.IsLetterOrDigit(c) && c < 128)
            {
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (c is '-' or '_' or '.' or ' ')
            {
                sb.Append('-');
            }
            // combining marks and other non-ASCII letters are dropped
        }

        var slug = Whitespace().Replace(sb.ToString(), "-");
        slug = InvalidChars().Replace(slug, "-");
        slug = ConsecutiveDashes().Replace(slug, "-");
        slug = slug.Trim('-');

        if (string.IsNullOrEmpty(slug))
        {
            slug = "project";
        }

        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex InvalidChars();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex ConsecutiveDashes();
}
