using System.Text.Json;

namespace HumbleRename.Cli;

/// <summary>
/// The menu choices remembered between runs — everything the main menu controls except
/// the Comic Vine key, which lives in its own encrypted file. Non-sensitive, so plain
/// JSON under %LOCALAPPDATA%, never in the app folder.
/// </summary>
public sealed record SessionSettings
{
    public string? Folder { get; init; }

    public int TemplateIndex { get; init; }

    public string? CustomTemplate { get; init; }

    public int FileTypeIndex { get; init; }

    public IReadOnlyList<string>? CustomExtensions { get; init; }

    public bool Recurse { get; init; }

    public bool Online { get; init; }

    public bool ReadMetadata { get; init; } = true;

    public bool HydrateCloudFiles { get; init; }

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HumbleRenamer",
        "settings.json");

    /// <summary>Reads saved settings, falling back to defaults when absent or unreadable.</summary>
    public static SessionSettings Load(string? path = null)
    {
        var target = path ?? DefaultPath;

        try
        {
            if (File.Exists(target))
            {
                var settings = JsonSerializer.Deserialize<SessionSettings>(File.ReadAllText(target));
                if (settings is not null)
                {
                    return settings;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable settings just fall back to defaults.
        }

        return new SessionSettings();
    }

    /// <summary>Writes the settings; a failure is swallowed since persistence is a convenience.</summary>
    public void Save(string? path = null)
    {
        var target = path ?? DefaultPath;

        try
        {
            var directory = Path.GetDirectoryName(target);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(target, JsonSerializer.Serialize(this));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing a settings write must never break the session.
        }
    }
}
