using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumbleRename.Renaming;

/// <summary>One completed rename, recorded so it can be put back.</summary>
public sealed record UndoEntry
{
    public required string Directory { get; init; }

    public required string From { get; init; }

    public required string To { get; init; }
}

/// <summary>
/// A record of one apply operation, written next to the renamed files.
/// </summary>
/// <remarks>
/// The interactive revert prompt covers the common case, but the log means a run can
/// still be undone tomorrow, or after the window has been closed.
/// </remarks>
public sealed record UndoLog
{
    /// <summary>Filename used inside the renamed folder.</summary>
    public const string FileName = ".hbrename-undo.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public required DateTimeOffset TimestampUtc { get; init; }

    public required string Root { get; init; }

    public required IReadOnlyList<UndoEntry> Entries { get; init; }

    public static string PathFor(string root) => Path.Combine(root, FileName);

    public void Save(string root)
    {
        var path = PathFor(root);
        File.WriteAllText(path, JsonSerializer.Serialize(this, SerializerOptions));

        // Keep the log out of the user's way; it is bookkeeping, not content.
        try
        {
            File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Hiding is cosmetic.
        }
    }

    public static UndoLog? Load(string root)
    {
        var path = PathFor(root);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<UndoLog>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Delete(string root)
    {
        var path = PathFor(root);
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Leaving a stale log behind is harmless.
        }
    }
}
