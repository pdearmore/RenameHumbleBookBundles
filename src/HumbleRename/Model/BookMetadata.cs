namespace HumbleRename.Model;

/// <summary>Where a piece of metadata came from, ordered by how much we trust it.</summary>
public enum MetadataSource
{
    /// <summary>Nothing found.</summary>
    None = 0,

    /// <summary>Derived from the filename alone.</summary>
    Filename = 1,

    /// <summary>Matched against an online catalogue.</summary>
    Online = 2,

    /// <summary>Read from inside the file (ComicInfo.xml, EXTH, OPF, PDF info).</summary>
    Embedded = 3,
}

/// <summary>
/// Bibliographic facts about one file, however they were obtained. Every field is
/// optional because real-world comic files are wildly inconsistent.
/// </summary>
public sealed record BookMetadata
{
    /// <summary>Full work title, e.g. "Nailbiter Vol. 1" or "Days Gone Bye".</summary>
    public string? Title { get; init; }

    /// <summary>Series name, e.g. "The Walking Dead".</summary>
    public string? Series { get; init; }

    /// <summary>Story-arc or volume subtitle, e.g. "Days Gone Bye".</summary>
    public string? Subtitle { get; init; }

    public string? Author { get; init; }

    public string? Publisher { get; init; }

    /// <summary>Publication year.</summary>
    public int? Year { get; init; }

    /// <summary>Collected-edition volume number.</summary>
    public int? Volume { get; init; }

    /// <summary>Single-issue number, kept as text to preserve "004" and "0".</summary>
    public string? Issue { get; init; }

    /// <summary>Book number for "Deluxe Edition Book 1" style releases.</summary>
    public int? Book { get; init; }

    public string? Isbn { get; init; }

    public string? Summary { get; init; }

    /// <summary>Edition markers such as "Deluxe Edition" or "Humble Exclusive".</summary>
    public IReadOnlyList<string> Editions { get; init; } = [];

    public MetadataSource Source { get; init; } = MetadataSource.None;

    /// <summary>True when the title appears cut off mid-word by an exporter.</summary>
    public bool LooksTruncated { get; init; }

    public bool HasTitle => !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Series);

    /// <summary>
    /// Overlays <paramref name="other"/> onto this record, filling only fields this
    /// record leaves empty. Used to layer filename guesses under embedded truth.
    /// </summary>
    public BookMetadata FillFrom(BookMetadata? other)
    {
        if (other is null)
        {
            return this;
        }

        return this with
        {
            Title = Coalesce(Title, other.Title),
            Series = Coalesce(Series, other.Series),
            Subtitle = Coalesce(Subtitle, other.Subtitle),
            Author = Coalesce(Author, other.Author),
            Publisher = Coalesce(Publisher, other.Publisher),
            Year = Year ?? other.Year,
            Volume = Volume ?? other.Volume,
            Issue = Coalesce(Issue, other.Issue),
            Book = Book ?? other.Book,
            Isbn = Coalesce(Isbn, other.Isbn),
            Summary = Coalesce(Summary, other.Summary),
            Editions = Editions.Count > 0 ? Editions : other.Editions,
            Source = (MetadataSource)Math.Max((int)Source, (int)other.Source),
        };
    }

    /// <summary>
    /// Overlays only the structural and identity fields of <paramref name="other"/>,
    /// never the title, series or subtitle.
    /// </summary>
    /// <remarks>
    /// Used when this record's title came from an authoritative source (embedded
    /// metadata or a catalogue). The filename may still be the only place a volume or
    /// issue number appears, so those are worth taking — but its title fragments are
    /// not. A file called "The Action Bible_ God's Redempt" whose metadata says
    /// "The Action Bible" must not have the clipped "God's Redempt" grafted back on.
    /// </remarks>
    public BookMetadata FillStructureFrom(BookMetadata? other)
    {
        if (other is null)
        {
            return this;
        }

        return this with
        {
            Author = Coalesce(Author, other.Author),
            Publisher = Coalesce(Publisher, other.Publisher),
            Year = Year ?? other.Year,
            Volume = Volume ?? other.Volume,
            Issue = Coalesce(Issue, other.Issue),
            Book = Book ?? other.Book,
            Isbn = Coalesce(Isbn, other.Isbn),
            Summary = Coalesce(Summary, other.Summary),
            Editions = Editions.Count > 0 ? Editions : other.Editions,
        };
    }

    private static string? Coalesce(string? preferred, string? fallback) =>
        string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
}
