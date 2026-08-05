using System.Text.Json;

namespace HumbleRename.Lookup;

/// <summary>
/// Comic Vine volume search — by far the best source for comics specifically, but it
/// requires a free API key. Supply one via <c>--comicvine-key</c> or the
/// <c>HUMBLERENAMER_COMICVINE_KEY</c> environment variable; without it the provider stays idle.
/// </summary>
public sealed class ComicVineProvider : HttpLookupProvider
{
    private const int MaxResults = 5;

    private readonly string? _apiKey;

    public ComicVineProvider(HttpClient client, string? apiKey = null) : base(client) =>
        _apiKey = string.IsNullOrWhiteSpace(apiKey)
            ? Environment.GetEnvironmentVariable("HUMBLERENAMER_COMICVINE_KEY")
            : apiKey;

    public override string Name => "comicvine";

    public override bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    public override async Task<IReadOnlyList<LookupResult>> SearchAsync(
        LookupQuery query,
        CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            return [];
        }

        var url = "https://comicvine.gamespot.com/api/search/" +
                  $"?api_key={Uri.EscapeDataString(_apiKey!)}" +
                  "&format=json&resources=volume" +
                  $"&query={Uri.EscapeDataString(query.Title)}" +
                  $"&limit={MaxResults}&field_list=name,start_year,publisher";

        using var document = await GetJsonAsync(url, cancellationToken).ConfigureAwait(false);

        if (document is null ||
            !document.RootElement.TryGetProperty("results", out var results) ||
            results.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var matches = new List<LookupResult>();
        foreach (var item in results.EnumerateArray())
        {
            var name = Text(item, "name");
            if (name is null)
            {
                continue;
            }

            string? publisher = null;
            if (item.TryGetProperty("publisher", out var publisherElement) &&
                publisherElement.ValueKind == JsonValueKind.Object)
            {
                publisher = Text(publisherElement, "name");
            }

            matches.Add(new LookupResult
            {
                Title = name,
                Publisher = publisher,
                Year = YearFrom(Text(item, "start_year")),
                Provider = Name,
            });
        }

        return matches;
    }
}
