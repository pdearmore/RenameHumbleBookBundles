using HumbleRename.Model;

namespace HumbleRename.Renaming;

/// <summary>What will happen to one file.</summary>
public enum RenameStatus
{
    /// <summary>The file will be renamed.</summary>
    Rename,

    /// <summary>The proposed name equals the current one.</summary>
    Unchanged,

    /// <summary>Renamed, but a suffix was added because the target already existed.</summary>
    Deduplicated,

    /// <summary>Left alone — unsupported type, or no usable title could be derived.</summary>
    Skipped,

    /// <summary>Something went wrong reading or renaming the file.</summary>
    Error,
}

/// <summary>
/// One name the tool derived for a file, and where it came from. Hand-review lays these
/// side by side so the user can pick the right derivation per file rather than accepting
/// the single merged guess.
/// </summary>
public sealed record NameCandidate
{
    /// <summary>Where this reading came from, e.g. "from filename", "from file metadata".</summary>
    public required string Label { get; init; }

    /// <summary>The rendered filename, including its extension.</summary>
    public required string Name { get; init; }

    /// <summary>Which evidence layer produced it.</summary>
    public MetadataSource Source { get; init; }
}

/// <summary>One file's proposed change.</summary>
public sealed record RenameAction
{
    public required string Directory { get; init; }

    public required string OriginalName { get; init; }

    public required string ProposedName { get; init; }

    public required RenameStatus Status { get; init; }

    public required BookMetadata Metadata { get; init; }

    /// <summary>Human-readable explanation shown in the preview (source of the title, warnings).</summary>
    public string? Note { get; init; }

    /// <summary>
    /// The distinct names the tool derived for this file, most-trusted first; the first
    /// entry corresponds to <see cref="ProposedName"/>. Offered during hand-review.
    /// </summary>
    public IReadOnlyList<NameCandidate> Candidates { get; init; } = [];

    public string OriginalPath => Path.Combine(Directory, OriginalName);

    public string ProposedPath => Path.Combine(Directory, ProposedName);

    /// <summary>True when applying this action changes something on disk.</summary>
    public bool IsChange => Status is RenameStatus.Rename or RenameStatus.Deduplicated;
}

/// <summary>The full set of proposed changes for one folder.</summary>
public sealed record RenamePlan
{
    public required string Root { get; init; }

    public required IReadOnlyList<RenameAction> Actions { get; init; }

    public int ChangeCount => Actions.Count(static a => a.IsChange);

    public int UnchangedCount => Actions.Count(static a => a.Status == RenameStatus.Unchanged);

    public int SkippedCount => Actions.Count(static a => a.Status is RenameStatus.Skipped or RenameStatus.Error);
}

/// <summary>Everything that controls how names are produced.</summary>
public sealed record RenameOptions
{
    /// <summary>Token template used to render the new name.</summary>
    public string Template { get; init; } = NameTemplate.Default;

    /// <summary>Recurse into subdirectories.</summary>
    public bool Recursive { get; init; }

    /// <summary>Consult online catalogues for missing or truncated titles.</summary>
    public bool UseOnlineLookup { get; init; }

    /// <summary>Read metadata from inside files.</summary>
    public bool UseEmbeddedMetadata { get; init; } = true;

    /// <summary>Download OneDrive/Dropbox placeholder files so their metadata can be read.</summary>
    public bool HydrateCloudFiles { get; init; }

    /// <summary>Extensions to consider. Empty means "every file".</summary>
    public IReadOnlySet<string> Extensions { get; init; } = new HashSet<string>(
        [".cbz", ".cbr", ".cb7", ".cbt", ".pdf", ".epub", ".mobi", ".azw3", ".zip", ".rar"],
        StringComparer.OrdinalIgnoreCase);

    /// <summary>Minimum confidence before an online match is accepted.</summary>
    public double MinimumConfidence { get; init; } = Lookup.LookupService.DefaultMinimumConfidence;
}
