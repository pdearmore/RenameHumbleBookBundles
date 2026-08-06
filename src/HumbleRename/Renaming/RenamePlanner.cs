using HumbleRename.Lookup;
using HumbleRename.Metadata;
using HumbleRename.Model;
using HumbleRename.Naming;

namespace HumbleRename.Renaming;

/// <summary>
/// Builds the list of proposed renames for a folder.
/// </summary>
/// <remarks>
/// Evidence is layered cheapest-first. The filename always produces a guess; embedded
/// metadata overrides it when present; an online catalogue is consulted only when the
/// result so far looks truncated or unusable. Nothing touches disk here — planning is
/// entirely separate from applying, which is what makes the preview trustworthy.
/// </remarks>
public sealed class RenamePlanner
{
    /// <summary>
    /// How closely an embedded title must resemble the filename before it is trusted.
    /// Set low enough that a subtitle the filename omits still passes ("Angels and
    /// Visitations" vs "Angels and Visitations: A Miscellany"), high enough that an
    /// unrelated production string does not.
    /// </summary>
    private const double MinimumMetadataAgreement = 0.45;

    private readonly NamingEngine _engine;
    private readonly MetadataExtractor _extractor;
    private readonly LookupService? _lookup;
    private readonly RenameOptions _options;

    public RenamePlanner(NamingEngine engine, RenameOptions options, LookupService? lookup = null)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _lookup = lookup;
        _extractor = new MetadataExtractor(options.HydrateCloudFiles);
    }

    public async Task<RenamePlan> BuildAsync(
        string root,
        IProgress<(int Done, int Total, string Current)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Folder not found: {root}");
        }

        var files = EnumerateFiles(root).ToList();
        var actions = new List<RenameAction>(files.Count);

        // Names promised to earlier files, so two sources cannot claim one target.
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < files.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            progress?.Report((i + 1, files.Count, Path.GetFileName(file)));

            actions.Add(await BuildActionAsync(file, claimed, cancellationToken).ConfigureAwait(false));
        }

        return new RenamePlan { Root = root, Actions = actions };
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        var option = _options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

        return Directory.EnumerateFiles(root, "*", option)
            .Where(path =>
            {
                var name = Path.GetFileName(path);

                // Never rename our own bookkeeping.
                if (name.Equals(UndoLog.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                if (_options.Extensions.Count == 0)
                {
                    return true;
                }

                return _options.Extensions.Contains(Path.GetExtension(path));
            })
            .OrderBy(static p => p, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<RenameAction> BuildActionAsync(
        string path,
        HashSet<string> claimed,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var originalName = Path.GetFileName(path);
        var extension = Path.GetExtension(path);
        var stem = Path.GetFileNameWithoutExtension(path);

        try
        {
            var notes = new List<string>();

            BookMetadata? embedded = null;
            if (_options.UseEmbeddedMetadata)
            {
                embedded = _extractor.Read(path, out _, out var skippedCloudFile);
                if (skippedCloudFile)
                {
                    notes.Add("cloud-only file, metadata not read");
                }
            }

            // The filename always yields a structured guess.
            var fromFilename = _engine.Parser.Parse(stem, embedded?.Author);

            var metadata = Combine(embedded, fromFilename, notes);

            // Only reach for the network when the local evidence is weak.
            BookMetadata? online = null;
            if (_lookup is not null && ShouldLookUp(metadata))
            {
                var resolved = await TryLookupAsync(metadata, cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                {
                    metadata = resolved.Value.Metadata;
                    online = metadata;
                    notes.Add($"matched {resolved.Value.Provider} ({resolved.Value.Score:P0})");
                }
                else if (metadata.LooksTruncated)
                {
                    notes.Add("title looks truncated, no confident match");
                }
            }
            else if (metadata.LooksTruncated)
            {
                notes.Add("title looks truncated; try --online");
            }

            // Every reading the tool produced, kept for hand-review even when one wins.
            var candidates = BuildCandidates(metadata, fromFilename, embedded, online, originalName, extension);

            if (!metadata.HasTitle)
            {
                return new RenameAction
                {
                    Directory = directory,
                    OriginalName = originalName,
                    ProposedName = originalName,
                    Status = RenameStatus.Skipped,
                    Metadata = metadata,
                    Note = "no usable title could be derived",
                    Candidates = candidates,
                };
            }

            var rendered = NameTemplate.Render(_options.Template, metadata);
            var safe = PathSafety.MakeSafeFileName(rendered);

            if (string.Equals(safe + extension, originalName, StringComparison.Ordinal))
            {
                return new RenameAction
                {
                    Directory = directory,
                    OriginalName = originalName,
                    ProposedName = originalName,
                    Status = RenameStatus.Unchanged,
                    Metadata = metadata,
                    Note = notes.Count > 0 ? string.Join("; ", notes) : null,
                    Candidates = candidates,
                };
            }

            var finalName = PathSafety.ResolveCollision(directory, safe, extension, claimed, path);
            var deduplicated = !string.Equals(finalName, safe + extension, StringComparison.Ordinal);

            if (deduplicated)
            {
                notes.Add("name already taken, suffixed");
            }

            return new RenameAction
            {
                Directory = directory,
                OriginalName = originalName,
                ProposedName = finalName,
                Status = deduplicated ? RenameStatus.Deduplicated : RenameStatus.Rename,
                Metadata = metadata,
                Note = notes.Count > 0 ? string.Join("; ", notes) : null,
                Candidates = candidates,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new RenameAction
            {
                Directory = directory,
                OriginalName = originalName,
                ProposedName = originalName,
                Status = RenameStatus.Error,
                Metadata = new BookMetadata(),
                Note = ex.Message,
                Candidates = [KeepCurrent(originalName)],
            };
        }
    }

    /// <summary>
    /// Collects every distinct name the tool derived for one file — the merged result,
    /// then each raw evidence layer, then the untouched original — most-trusted first
    /// and deduplicated. Hand-review offers exactly this list.
    /// </summary>
    private List<NameCandidate> BuildCandidates(
        BookMetadata chosen,
        BookMetadata fromFilename,
        BookMetadata? embedded,
        BookMetadata? online,
        string originalName,
        string extension)
    {
        var list = new List<NameCandidate>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string label, MetadataSource source, string? name)
        {
            if (string.IsNullOrEmpty(name) || !seen.Add(name))
            {
                return;
            }

            list.Add(new NameCandidate { Label = label, Name = name, Source = source });
        }

        // The winning reading leads, so its name matches ProposedName.
        Add(SourceLabel(chosen.Source), chosen.Source, RenderName(chosen, extension));

        if (online is not null)
        {
            Add("from online catalogue", MetadataSource.Online, RenderName(online, extension));
        }

        // Show the file's own title even when agreement rejected it: it is still one of
        // the ways the name was derived, and the user may know better than the gate.
        if (embedded is not null && !string.IsNullOrWhiteSpace(embedded.Title))
        {
            Add("from file metadata", MetadataSource.Embedded,
                RenderName(MergeEmbedded(embedded, fromFilename), extension));
        }

        Add("from filename", MetadataSource.Filename, RenderName(fromFilename, extension));
        Add("keep current name", MetadataSource.None, originalName);

        return list;
    }

    private static NameCandidate KeepCurrent(string originalName) =>
        new() { Label = "keep current name", Name = originalName, Source = MetadataSource.None };

    private static string SourceLabel(MetadataSource source) => source switch
    {
        MetadataSource.Embedded => "from file metadata",
        MetadataSource.Online => "from online catalogue",
        MetadataSource.Filename => "from filename",
        _ => "best guess",
    };

    /// <summary>Renders one reading to a safe filename with its extension, or empty if untitled.</summary>
    private string RenderName(BookMetadata? metadata, string extension)
    {
        if (metadata is null || !metadata.HasTitle)
        {
            return string.Empty;
        }

        var safe = PathSafety.MakeSafeFileName(NameTemplate.Render(_options.Template, metadata));
        return safe.Length == 0 ? string.Empty : safe + extension;
    }

    /// <summary>
    /// Rebuilds a plan from the names a user chose during hand-review, keyed by the
    /// action's position. Absent entries keep the scan's proposal; collisions across the
    /// revised set are re-resolved so two hand-picked names cannot claim one target.
    /// </summary>
    public static RenamePlan RebuildWithChosenNames(
        RenamePlan plan,
        IReadOnlyDictionary<int, string> chosenBaseNames)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(chosenBaseNames);

        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var actions = new List<RenameAction>(plan.Actions.Count);

        for (var i = 0; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i];
            var picked = chosenBaseNames.TryGetValue(i, out var choice) ? choice : null;

            // A file the tool could not name and the user did not touch stays skipped.
            if (picked is null && action.Status is RenameStatus.Skipped or RenameStatus.Error)
            {
                actions.Add(action);
                continue;
            }

            var extension = Path.GetExtension(action.OriginalName);
            var desiredBase = !string.IsNullOrWhiteSpace(picked)
                ? PathSafety.MakeSafeFileName(picked)
                : Path.GetFileNameWithoutExtension(action.ProposedName);

            var finalName = PathSafety.ResolveCollision(
                action.Directory, desiredBase, extension, claimed, action.OriginalPath);
            var deduplicated = !string.Equals(finalName, desiredBase + extension, StringComparison.Ordinal);
            var status = string.Equals(finalName, action.OriginalName, StringComparison.Ordinal)
                ? RenameStatus.Unchanged
                : deduplicated ? RenameStatus.Deduplicated : RenameStatus.Rename;

            actions.Add(action with
            {
                ProposedName = finalName,
                Status = status,
                Note = deduplicated ? "name already taken, suffixed" : null,
            });
        }

        return plan with { Actions = actions };
    }

    /// <summary>
    /// Merges embedded metadata with the filename guess. An embedded title is itself
    /// run back through the parser, because publishers write "Nailbiter Vol. 1" into
    /// the title field and we still need that split into series and volume.
    /// </summary>
    private BookMetadata Combine(BookMetadata? embedded, BookMetadata fromFilename, List<string> notes)
    {
        if (embedded is null || string.IsNullOrWhiteSpace(embedded.Title))
        {
            return fromFilename;
        }

        // Embedded metadata is normally the better source, but PDFs routinely carry a
        // production artefact in the title field — "Print", "AHE Final Text" — and one
        // of those applied blindly renames a correctly named file after a different
        // book. Require the two to at least describe the same work.
        var filenameTitle = fromFilename.Title ?? fromFilename.Series;
        if (!string.IsNullOrWhiteSpace(filenameTitle))
        {
            var agreement = TitleSimilarity.Compare(filenameTitle, embedded.Title!);
            if (agreement < MinimumMetadataAgreement)
            {
                notes.Add($"ignored file metadata \"{Ellipsis(embedded.Title!)}\", it disagrees with the filename");
                return fromFilename;
            }
        }

        notes.Add("title from file metadata");
        return MergeEmbedded(embedded, fromFilename);
    }

    /// <summary>
    /// Structures an embedded title and layers the filename's structural fields under it.
    /// Shared by <see cref="Combine"/> and the candidate builder so both render an
    /// accepted file-metadata title identically.
    /// </summary>
    private BookMetadata MergeEmbedded(BookMetadata embedded, BookMetadata fromFilename)
    {
        // Both callers gate on a non-empty embedded title before reaching here.
        var structured = _engine.Parser.Parse(embedded.Title!, embedded.Author);

        var merged = structured with
        {
            Author = embedded.Author ?? structured.Author ?? fromFilename.Author,
            Publisher = embedded.Publisher,
            Isbn = embedded.Isbn ?? fromFilename.Isbn,
            Summary = embedded.Summary,
            Year = embedded.Year ?? structured.Year,
            Source = MetadataSource.Embedded,
        };

        // Volume and issue numbers often live only in the filename, so take those —
        // but not title fragments, which are exactly what the exporter clipped.
        return merged.FillStructureFrom(fromFilename);
    }

    /// <summary>Shortens a value for display in a preview note.</summary>
    private static string Ellipsis(string value, int max = 40) =>
        value.Length <= max ? value : value[..(max - 1)] + "…";

    /// <summary>
    /// True when the local evidence is weak enough to justify a network round trip.
    /// </summary>
    private static bool ShouldLookUp(BookMetadata metadata) =>
        metadata.LooksTruncated ||
        !metadata.HasTitle ||
        !string.IsNullOrWhiteSpace(metadata.Isbn);

    private async Task<(BookMetadata Metadata, string Provider, double Score)?> TryLookupAsync(
        BookMetadata metadata,
        CancellationToken cancellationToken)
    {
        var title = metadata.Title ?? metadata.Series;
        if (string.IsNullOrWhiteSpace(title) || _lookup is null)
        {
            return null;
        }

        var query = new LookupQuery(
            title,
            metadata.Author,
            metadata.Volume,
            metadata.Isbn,
            metadata.LooksTruncated);

        var match = await _lookup.IdentifyAsync(query, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return null;
        }

        return (ResolveMatch(match, metadata), match.Provider, match.Score);
    }

    /// <summary>
    /// Re-parses a catalogue match into structured metadata, layering the file's own
    /// structural fields (volume, issue) underneath — the catalogue supplies the title,
    /// the filename keeps the number. Shared by the batch lookup and the on-demand
    /// hand-review lookup so both render an online title identically.
    /// </summary>
    private BookMetadata ResolveMatch(LookupResult match, BookMetadata metadata)
    {
        var structured = _engine.Parser.Parse(match.Title, match.Author ?? metadata.Author);

        return (structured with
        {
            Author = metadata.Author ?? match.Author,
            Publisher = metadata.Publisher ?? match.Publisher,
            Year = metadata.Year ?? match.Year,
            Isbn = metadata.Isbn,
            Source = MetadataSource.Online,
            LooksTruncated = false,
        }).FillStructureFrom(metadata);
    }

    /// <summary>
    /// Queries the catalogues for one already-planned file and returns the best match as
    /// a selectable name candidate, or null when nothing clears the confidence floor.
    /// Drives hand-review's on-demand lookup: it takes an explicit
    /// <paramref name="lookup"/> so it works even when the batch online setting was off.
    /// </summary>
    public async Task<NameCandidate?> LookUpOnlineAsync(
        RenameAction action,
        LookupService lookup,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(lookup);

        var metadata = action.Metadata;
        var title = metadata.Title ?? metadata.Series;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var query = new LookupQuery(
            title, metadata.Author, metadata.Volume, metadata.Isbn, metadata.LooksTruncated);

        var match = await lookup.IdentifyAsync(query, cancellationToken).ConfigureAwait(false);
        if (match is null)
        {
            return null;
        }

        var name = RenderName(ResolveMatch(match, metadata), Path.GetExtension(action.OriginalName));
        return string.IsNullOrEmpty(name)
            ? null
            : new NameCandidate { Label = "from online catalogue", Name = name, Source = MetadataSource.Online };
    }
}
