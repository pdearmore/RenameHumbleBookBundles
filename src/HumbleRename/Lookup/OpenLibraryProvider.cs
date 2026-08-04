using System.Text.Json;

namespace HumbleRename.Lookup;

/// <summary>
/// Open Library search. Keyless, generous with rate limits, and strong on collected
/// editions and graphic novels — the default provider for that reason.
/// </summary>
public sealed class OpenLibraryProvider : HttpLookupProvider
{
    private const int MaxResults = 5;

    public OpenLibraryProvider(HttpClient client) : base(client)
    {
    }

    public override string Name => "openlibrary";

    public override async Task<IReadOnlyList<LookupResult>> SearchAsync(
        LookupQuery query,
        CancellationToken cancellationToken)
    {
        var url = BuildUrl(query);
        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

        if (document is null ||
            !document.RootElement.TryGetProperty("docs", out var docs) ||
            docs.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<LookupResult>();
        foreach (var doc in docs.EnumerateArray())
        {
            var title = Text(doc, "title");
            if (title is null)
            {
                continue;
            }

            // Open Library keeps the subtitle in a separate field.
            var subtitle = Text(doc, "subtitle");
            var fullTitle = subtitle is null ? title : $"{title}: {subtitle}";

            results.Add(new LookupResult
            {
                Title = fullTitle,
                Author = FirstOfArray(doc, "author_name"),
                Publisher = FirstOfArray(doc, "publisher"),
                Year = doc.TryGetProperty("first_publish_year", out var year) &&
                       year.ValueKind == JsonValueKind.Number
                    ? year.GetInt32()
                    : null,
                Provider = Name,
            });
        }

        return results;
    }

    private static string BuildUrl(LookupQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Isbn))
        {
            return $"https://openlibrary.org/search.json?q=isbn:{Uri.EscapeDataString(query.Isbn)}" +
                   $"&limit={MaxResults}&fields=title,subtitle,author_name,first_publish_year,publisher";
        }

        var url = $"https://openlibrary.org/search.json?title={Uri.EscapeDataString(query.Title)}" +
                  $"&limit={MaxResults}&fields=title,subtitle,author_name,first_publish_year,publisher";

        if (!string.IsNullOrWhiteSpace(query.Author))
        {
            url += $"&author={Uri.EscapeDataString(query.Author)}";
        }

        return url;
    }
}
