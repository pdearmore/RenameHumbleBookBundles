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
            if (_lookup is not null && ShouldLookUp(metadata))
            {
                var resolved = await TryLookupAsync(metadata, cancellationToken).ConfigureAwait(false);
                if (resolved is not null)
                {
                    metadata = resolved.Value.Metadata;
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
            };
        }
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

        var structured = _engine.Parser.Parse(embedded.Title, embedded.Author);

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
        merged = merged.FillStructureFrom(fromFilename);

        notes.Add("title from file metadata");
        return merged;
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

        // Re-parse the catalogue title so it gains the same structure as everything else.
        var structured = _engine.Parser.Parse(match.Title, match.Author ?? metadata.Author);

        var resolved = (structured with
        {
            Author = metadata.Author ?? match.Author,
            Publisher = metadata.Publisher ?? match.Publisher,
            Year = metadata.Year ?? match.Year,
            Isbn = metadata.Isbn,
            Source = MetadataSource.Online,
            LooksTruncated = false,
        }).FillStructureFrom(metadata);

        return (resolved, match.Provider, match.Score);
    }
}
