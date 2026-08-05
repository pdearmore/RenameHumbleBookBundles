namespace HumbleRename.Lookup;

/// <summary>What we know about a book when asking a catalogue to identify it.</summary>
/// <param name="Title">Best-guess title, possibly truncated.</param>
/// <param name="Author">Author, when known.</param>
/// <param name="Volume">Collected-volume number, when known.</param>
/// <param name="Isbn">ISBN, when known — the strongest possible signal.</param>
/// <param name="Truncated">True when <paramref name="Title"/> is known to be cut short.</param>
public sealed record LookupQuery(
    string Title,
    string? Author = null,
    int? Volume = null,
    string? Isbn = null,
    bool Truncated = false);

/// <summary>One candidate match returned by a catalogue.</summary>
public sealed record LookupResult
{
    public required string Title { get; init; }

    public string? Author { get; init; }

    public string? Publisher { get; init; }

    public int? Year { get; init; }

    /// <summary>Name of the provider that supplied this candidate.</summary>
    public required string Provider { get; init; }

    /// <summary>Confidence in [0,1], assigned by <see cref="TitleSimilarity"/>.</summary>
    public double Score { get; set; }
}

/// <summary>A catalogue HumbleRenamer can query for real titles.</summary>
public interface ILookupProvider
{
    /// <summary>Short name shown in output and used as a cache key prefix.</summary>
    string Name { get; }

    /// <summary>True when the provider has whatever configuration it needs (e.g. an API key).</summary>
    bool IsConfigured { get; }

    Task<IReadOnlyList<LookupResult>> SearchAsync(LookupQuery query, CancellationToken cancellationToken);
}
