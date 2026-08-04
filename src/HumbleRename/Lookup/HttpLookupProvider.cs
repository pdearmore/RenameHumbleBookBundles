using System.Net;
using System.Text.Json;

namespace HumbleRename.Lookup;

/// <summary>
/// Shared HTTP plumbing for catalogue providers: a polite user agent, bounded
/// retries, and 429 handling that respects <c>Retry-After</c>.
/// </summary>
public abstract class HttpLookupProvider : ILookupProvider
{
    /// <summary>Public catalogues throttle hard; three attempts is plenty before moving on.</summary>
    private const int MaxAttempts = 3;

    private readonly HttpClient _client;

    protected HttpLookupProvider(HttpClient client) =>
        _client = client ?? throw new ArgumentNullException(nameof(client));

    public abstract string Name { get; }

    public virtual bool IsConfigured => true;

    public abstract Task<IReadOnlyList<LookupResult>> SearchAsync(
        LookupQuery query,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches and parses JSON, returning <c>null</c> rather than throwing when the
    /// catalogue is unavailable — a failed lookup must never fail the rename run.
    /// </summary>
    protected async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var response = await _client
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
                {
                    if (attempt == MaxAttempts)
                    {
                        return null;
                    }

                    var delay = response.Headers.RetryAfter?.Delta
                                ?? TimeSpan.FromMilliseconds(400 * Math.Pow(2, attempt));

                    // Cap the wait: a scan of a large folder should not stall for minutes.
                    await Task.Delay(
                            TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, 4000)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException)
            {
                if (attempt == MaxAttempts)
                {
                    return null;
                }
            }
        }

        return null;
    }

    /// <summary>Reads a string property, treating empty values as absent.</summary>
    protected static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    /// <summary>Reads the first entry of a string array property.</summary>
    protected static string? FirstOfArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                return item.GetString();
            }
        }

        return null;
    }

    /// <summary>Extracts a 4-digit year from a date string of any shape.</summary>
    protected static int? YearFrom(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        for (var i = 0; i + 4 <= value.Length; i++)
        {
            if (int.TryParse(value.AsSpan(i, 4), out var year) && year is >= 1800 and <= 2200)
            {
                return year;
            }
        }

        return null;
    }
}
