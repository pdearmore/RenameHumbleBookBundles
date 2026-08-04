using System.Net;
using System.Net.Http.Headers;

namespace HumbleRename.Lookup;

/// <summary>
/// Queries catalogues in order of expected quality and returns the best-scoring match
/// above a confidence floor, or nothing at all.
/// </summary>
/// <remarks>
/// Returning nothing is a perfectly good outcome: a wrong title confidently applied to
/// a hundred files is far worse than leaving the filename-derived guess in place.
/// </remarks>
public sealed class LookupService : IDisposable
{
    /// <summary>Below this score a candidate is discarded rather than applied.</summary>
    public const double DefaultMinimumConfidence = 0.72;

    /// <summary>Good enough to stop asking further providers.</summary>
    private const double ShortCircuitConfidence = 0.90;

    private readonly HttpClient _client;
    private readonly List<ILookupProvider> _providers;
    private readonly LookupCache _cache;
    private readonly double _minimumConfidence;

    private LookupService(
        HttpClient client,
        List<ILookupProvider> providers,
        LookupCache cache,
        double minimumConfidence)
    {
        _client = client;
        _providers = providers;
        _cache = cache;
        _minimumConfidence = minimumConfidence;
    }

    /// <summary>Providers that will actually be queried, in priority order.</summary>
    public IEnumerable<string> ActiveProviders => _providers.Select(static p => p.Name);

    public static LookupService Create(
        string? comicVineKey = null,
        string? googleBooksKey = null,
        double minimumConfidence = DefaultMinimumConfidence,
        string? cachePath = null,
        TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };

        var client = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(15),
        };

        // Comic Vine rejects requests without a descriptive user agent.
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("hbrename", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        // Ordered best-first for comics: Comic Vine knows the medium, Open Library is
        // the most reliable keyless option, Google Books is the broadest but throttles.
        var providers = new List<ILookupProvider>
        {
            new ComicVineProvider(client, comicVineKey),
            new OpenLibraryProvider(client),
            new GoogleBooksProvider(client, googleBooksKey),
        };

        providers.RemoveAll(static p => !p.IsConfigured);

        return new LookupService(client, providers, LookupCache.Load(cachePath), minimumConfidence);
    }

    /// <summary>
    /// Returns the best match for <paramref name="query"/>, or <c>null</c> when no
    /// candidate clears the confidence floor.
    /// </summary>
    public async Task<LookupResult?> IdentifyAsync(LookupQuery query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query.Title))
        {
            return null;
        }

        LookupResult? best = null;

        foreach (var provider in _providers)
        {
            var candidates = await GetCandidatesAsync(provider, query, cancellationToken).ConfigureAwait(false);

            foreach (var candidate in candidates)
            {
                candidate.Score = TitleSimilarity.Score(query, candidate);
                if (best is null || candidate.Score > best.Score)
                {
                    best = candidate;
                }
            }

            if (best is not null && best.Score >= ShortCircuitConfidence)
            {
                break;
            }
        }

        return best is not null && best.Score >= _minimumConfidence ? best : null;
    }

    private async Task<IReadOnlyList<LookupResult>> GetCandidatesAsync(
        ILookupProvider provider,
        LookupQuery query,
        CancellationToken cancellationToken)
    {
        var cacheKey = BuildCacheKey(query);

        if (_cache.TryGet(provider.Name, cacheKey, out var cached))
        {
            // Scores are recomputed per query, so cache only the raw candidates.
            return cached.Select(static r => r with { }).ToList();
        }

        var results = await provider.SearchAsync(query, cancellationToken).ConfigureAwait(false);
        _cache.Set(provider.Name, cacheKey, results);
        return results;
    }

    private static string BuildCacheKey(LookupQuery query) =>
        string.IsNullOrWhiteSpace(query.Isbn)
            ? $"{query.Title}|{query.Author}"
            : $"isbn:{query.Isbn}";

    /// <summary>Persists the cache. Safe to call more than once.</summary>
    public void Flush() => _cache.Save();

    public void Dispose()
    {
        Flush();
        _client.Dispose();
    }
}
