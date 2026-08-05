using System.Text;

namespace HumbleRename.Renaming;

/// <summary>
/// Turns a proposed title into something Windows will actually accept as a filename.
/// </summary>
public static class PathSafety
{
    /// <summary>
    /// Device names that cannot be used as a filename even with an extension.
    /// </summary>
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Conservative cap on the name portion. The full path also matters, but a limit
    /// here keeps titles readable and leaves room for a collision suffix.
    /// </summary>
    public const int MaxNameLength = 150;

    /// <summary>
    /// Replaces characters Windows forbids, using substitutions that read naturally
    /// rather than dropping information silently.
    /// </summary>
    public static string MakeSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Untitled";
        }

        var builder = new StringBuilder(name.Length);

        foreach (var c in name)
        {
            switch (c)
            {
                // A colon almost always separates a title from its subtitle.
                case ':':
                    builder.Append(" -");
                    break;
                case '/':
                case '\\':
                    builder.Append('-');
                    break;
                case '"':
                    builder.Append('\'');
                    break;
                case '|':
                    builder.Append('-');
                    break;
                case '*':
                    builder.Append('+');
                    break;
                case '<':
                    builder.Append('(');
                    break;
                case '>':
                    builder.Append(')');
                    break;
                // Question marks are part of real titles ("Can You Just Die, My Darling?")
                // but cannot be written, so they simply go.
                case '?':
                    break;
                default:
                    builder.Append(char.IsControl(c) ? ' ' : c);
                    break;
            }
        }

        var result = CollapseWhitespace(builder.ToString());

        // Windows silently strips trailing dots and spaces, which would desynchronise
        // our record of the name from what actually lands on disk.
        result = result.TrimEnd('.', ' ');

        if (result.Length > MaxNameLength)
        {
            result = result[..MaxNameLength].TrimEnd('.', ' ', '-', ',');
        }

        if (result.Length == 0)
        {
            return "Untitled";
        }

        if (ReservedNames.Contains(result))
        {
            result = "_" + result;
        }

        return result;
    }

    /// <summary>
    /// Finds a free path for <paramref name="desiredName"/> in <paramref name="directory"/>,
    /// appending " (2)", " (3)" and so on. <paramref name="claimed"/> holds names already
    /// promised to earlier files in the same run.
    /// </summary>
    public static string ResolveCollision(
        string directory,
        string desiredName,
        string extension,
        ISet<string> claimed,
        string? currentPath = null)
    {
        var candidate = desiredName + extension;

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var fullPath = Path.Combine(directory, candidate);

            var takenOnDisk = File.Exists(fullPath) &&
                              !string.Equals(fullPath, currentPath, StringComparison.OrdinalIgnoreCase);
            var takenInRun = claimed.Contains(candidate);

            if (!takenOnDisk && !takenInRun)
            {
                claimed.Add(candidate);
                return candidate;
            }

            candidate = $"{desiredName} ({suffix}){extension}";
        }

        // Pathological case: fall back to something guaranteed unique.
        var unique = $"{desiredName} ({Guid.NewGuid():N}){extension}";
        claimed.Add(unique);
        return unique;
    }

    private static string CollapseWhitespace(string value)
    {
        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
