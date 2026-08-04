using System.Text;
using HumbleRename.Model;

namespace HumbleRename.Renaming;

/// <summary>
/// Renders a filename from metadata using a token template.
/// </summary>
/// <remarks>
/// <para>
/// Tokens are <c>{Name}</c>, optionally with a numeric format: <c>{Volume:00}</c>.
/// A section wrapped in square brackets is dropped entirely when any token inside it
/// is empty, which is what lets one template serve files that have a volume, files
/// that have an issue, and files that have neither.
/// </para>
/// <para>
/// Recognised tokens: Series, Title, Subtitle, Volume, Issue, Book, Year, Author,
/// Publisher, Editions.
/// </para>
/// </remarks>
public static class NameTemplate
{
    /// <summary>
    /// The default layout: series, then volume/book/issue, then story-arc subtitle,
    /// then year, then any edition markers.
    /// </summary>
    public const string Default =
        "{Series}[ Vol. {Volume:00}][ Book {Book}][ #{Issue}][ - {Subtitle}][ ({Year})][ ({Editions})]";

    /// <summary>A shorter layout that omits the subtitle and edition markers.</summary>
    public const string Compact = "{Series}[ v{Volume:00}][ #{Issue}][ ({Year})]";

    /// <summary>Renders <paramref name="template"/> against <paramref name="metadata"/>.</summary>
    public static string Render(string template, BookMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (string.IsNullOrWhiteSpace(template))
        {
            template = Default;
        }

        var output = new StringBuilder();
        RenderSection(template, metadata, output, isOptional: false, out _);
        return CollapseSpaces(output.ToString()).Trim(' ', '-', ',', ':');
    }

    /// <summary>
    /// Renders a run of template text. Returns via <paramref name="anyTokenEmpty"/>
    /// whether a token resolved to nothing, so the caller can drop an optional section.
    /// </summary>
    private static void RenderSection(
        string template,
        BookMetadata metadata,
        StringBuilder output,
        bool isOptional,
        out bool anyTokenEmpty)
    {
        anyTokenEmpty = false;
        var local = new StringBuilder();

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];

            if (c == '[')
            {
                var end = FindMatchingBracket(template, i);
                if (end < 0)
                {
                    local.Append(c);
                    continue;
                }

                var inner = template[(i + 1)..end];
                var nested = new StringBuilder();
                RenderSection(inner, metadata, nested, isOptional: true, out var innerEmpty);

                // The whole bracketed group survives only if every token inside filled in.
                if (!innerEmpty)
                {
                    local.Append(nested);
                }

                i = end;
                continue;
            }

            if (c == '{')
            {
                var end = template.IndexOf('}', i + 1);
                if (end < 0)
                {
                    local.Append(c);
                    continue;
                }

                var token = template[(i + 1)..end];
                var value = ResolveToken(token, metadata);

                if (string.IsNullOrEmpty(value))
                {
                    anyTokenEmpty = true;
                }
                else
                {
                    local.Append(value);
                }

                i = end;
                continue;
            }

            local.Append(c);
        }

        if (!isOptional || !anyTokenEmpty)
        {
            output.Append(local);
        }
    }

    private static int FindMatchingBracket(string template, int openIndex)
    {
        var depth = 0;
        for (var i = openIndex; i < template.Length; i++)
        {
            switch (template[i])
            {
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        return i;
                    }

                    break;
            }
        }

        return -1;
    }

    private static string? ResolveToken(string token, BookMetadata metadata)
    {
        var colon = token.IndexOf(':');
        var name = colon > 0 ? token[..colon] : token;
        var format = colon > 0 ? token[(colon + 1)..] : null;

        return name.ToLowerInvariant() switch
        {
            "series" => metadata.Series ?? metadata.Title,
            "title" => metadata.Title ?? metadata.Series,
            "subtitle" => metadata.Subtitle,
            "author" => metadata.Author,
            "publisher" => metadata.Publisher,
            "editions" => metadata.Editions.Count > 0 ? string.Join(", ", metadata.Editions) : null,
            "volume" => FormatNumber(metadata.Volume, format),
            "book" => FormatNumber(metadata.Book, format),
            "year" => FormatNumber(metadata.Year, null),
            "issue" => FormatIssue(metadata.Issue, format),
            _ => null,
        };
    }

    private static string? FormatNumber(int? value, string? format)
    {
        if (value is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(format)
            ? value.Value.ToString()
            : value.Value.ToString(format);
    }

    /// <summary>
    /// Issues stay textual so "004" and "0" survive, but a format string still applies
    /// when the issue is purely numeric.
    /// </summary>
    private static string? FormatIssue(string? issue, string? format)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            return null;
        }

        if (!string.IsNullOrEmpty(format) && int.TryParse(issue, out var numeric))
        {
            return numeric.ToString(format);
        }

        return issue;
    }

    private static string CollapseSpaces(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (c == ' ')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
