using System.Text;
using System.Text.Json;

namespace HumbleRename.Lookup;

/// <summary>
/// Google Books search. Works without a key but throttles per source address, so it
/// is queried after Open Library. Set <c>HUMBLERENAMER_GOOGLE_BOOKS_KEY</c> to raise the quota.
/// </summary>
public sealed class GoogleBooksProvider : HttpLookupProvider
{
    private const int MaxResults = 5;

    private readonly string? _apiKey;

    public GoogleBooksProvider(HttpClient client, string? apiKey = null) : base(client) =>
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable("HUMBLERENAMER_GOOGLE_BOOKS_KEY")
            : apiKey;

    public override string Name => "googlebooks";

    public override async Task<IReadOnlyList<LookupResult>> SearchAsync(
        LookupQuery query,
        CancellationToken cancellationToken)
    {
        using var document = await GetJsonAsync(BuildUrl(query), cancellationToken).ConfigureAwait(false);

        if (document is null ||
            !document.RootElement.TryGetProperty("items", out var items) ||
            items.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var results = new List<LookupResult>();
        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("volumeInfo", out var info))
            {
                continue;
            }

            var title = Text(info, "title");
            if (title is null)
            {
                continue;
            }

            var subtitle = Text(info, "subtitle");
            var fullTitle = subtitle is null ? title : $"{title}: {subtitle}";

            results.Add(new LookupResult
            {
                Title = fullTitle,
                Author = FirstOfArray(info, "authors"),
                Publisher = Text(info, "publisher"),
                Year = YearFrom(Text(info, "publishedDate")),
                Provider = Name,
            });
        }

        return results;
    }

    private string BuildUrl(LookupQuery query)
    {
        var terms = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(query.Isbn))
        {
            terms.Append("isbn:").Append(query.Isbn);
        }
        else
        {
            terms.Append("intitle:").Append('"').Append(query.Title).Append('"');
            if (!string.IsNullOrWhiteSpace(query.Author))
            {
                terms.Append("+inauthor:").Append('"').Append(query.Author).Append('"');
            }
        }

        var url = "https://www.googleapis.com/books/v1/volumes?q=" +
                  Uri.EscapeDataString(terms.ToString()) +
                  $"&maxResults={MaxResults}&printType=books";

        if (!string.IsNullOrWhiteSpace(_apiKey))
        {
            url += $"&key={Uri.EscapeDataString(_apiKey)}";
        }

        return url;
    }
}
