namespace HumbleRename.Renaming;

/// <summary>Outcome of applying or reverting a plan.</summary>
public sealed record RenameOutcome
{
    public required int Succeeded { get; init; }

    public required IReadOnlyList<string> Failures { get; init; }

    public UndoLog? Undo { get; init; }
}

/// <summary>
/// Applies a <see cref="RenamePlan"/> to disk and reverses it again.
/// </summary>
public static class RenameExecutor
{
    /// <summary>
    /// Performs every change in <paramref name="plan"/>, recording an undo log.
    /// A failure on one file does not stop the rest.
    /// </summary>
    public static RenameOutcome Apply(RenamePlan plan, Action<RenameAction, Exception?>? onEach = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var entries = new List<UndoEntry>();
        var failures = new List<string>();

        foreach (var action in plan.Actions.Where(static a => a.IsChange))
        {
            try
            {
                // A rename that differs only in case needs a two-step move, because
                // Windows treats the source and destination as the same file.
                if (string.Equals(action.OriginalName, action.ProposedName, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(action.OriginalName, action.ProposedName, StringComparison.Ordinal))
                {
                    var temporary = Path.Combine(
                        action.Directory,
                        action.OriginalName + ".hbrename-tmp");

                    File.Move(action.OriginalPath, temporary);
                    File.Move(temporary, action.ProposedPath);
                }
                else
                {
                    File.Move(action.OriginalPath, action.ProposedPath);
                }

                entries.Add(new UndoEntry
                {
                    Directory = action.Directory,
                    From = action.OriginalName,
                    To = action.ProposedName,
                });

                onEach?.Invoke(action, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{action.OriginalName}: {ex.Message}");
                onEach?.Invoke(action, ex);
            }
        }

        UndoLog? log = null;
        if (entries.Count > 0)
        {
            log = new UndoLog
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Root = plan.Root,
                Entries = entries,
            };

            try
            {
                log.Save(plan.Root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"Could not write the undo log: {ex.Message}");
            }
        }

        return new RenameOutcome
        {
            Succeeded = entries.Count,
            Failures = failures,
            Undo = log,
        };
    }

    /// <summary>
    /// Moves every file in <paramref name="log"/> back to its original name.
    /// </summary>
    public static RenameOutcome Revert(UndoLog log, Action<UndoEntry, Exception?>? onEach = null)
    {
        ArgumentNullException.ThrowIfNull(log);

        var reverted = 0;
        var failures = new List<string>();

        // Reverse order so any chain of renames unwinds cleanly.
        foreach (var entry in log.Entries.Reverse())
        {
            var current = Path.Combine(entry.Directory, entry.To);
            var original = Path.Combine(entry.Directory, entry.From);

            try
            {
                if (!File.Exists(current))
                {
                    failures.Add($"{entry.To}: no longer present, skipped");
                    onEach?.Invoke(entry, null);
                    continue;
                }

                if (File.Exists(original) &&
                    !string.Equals(current, original, StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add($"{entry.From}: something already has that name, skipped");
                    continue;
                }

                if (string.Equals(entry.To, entry.From, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(entry.To, entry.From, StringComparison.Ordinal))
                {
                    var temporary = current + ".hbrename-tmp";
                    File.Move(current, temporary);
                    File.Move(temporary, original);
                }
                else
                {
                    File.Move(current, original);
                }

                reverted++;
                onEach?.Invoke(entry, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add($"{entry.To}: {ex.Message}");
                onEach?.Invoke(entry, ex);
            }
        }

        return new RenameOutcome
        {
            Succeeded = reverted,
            Failures = failures,
        };
    }
}
