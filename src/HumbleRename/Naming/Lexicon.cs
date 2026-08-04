using System.Reflection;

namespace HumbleRename.Naming;

/// <summary>
/// Curated domain vocabulary: known titles, proper nouns, acronyms, junk tokens
/// and edition markers. Loaded from the embedded <c>Data/lexicon.txt</c> and
/// optionally merged with a user file so a bad guess can be corrected without a rebuild.
/// </summary>
public sealed class Lexicon
{
    /// <summary>Despaced lowercase title key to properly cased title.</summary>
    public Dictionary<string, string> Titles { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Proper nouns injected into the segmenter's vocabulary.</summary>
    public HashSet<string> Words { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tokens that render in all caps.</summary>
    public HashSet<string> Uppercase { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Articles/prepositions lowercased mid-title.</summary>
    public HashSet<string> SmallWords { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tokens removed outright (scene tags, export artefacts).</summary>
    public HashSet<string> Junk { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Edition marker key to rendered form.</summary>
    public Dictionary<string, string> Editions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Normalises a title to its lookup key: lowercase, letters and digits only.
    /// "X-O Manowar" and "xomanowar" therefore collapse to the same key.
    /// </summary>
    public static string Key(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var buffer = new char[value.Length];
        var length = 0;
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }

        return new string(buffer, 0, length);
    }

    /// <summary>Looks up a known title by any spacing/punctuation variant.</summary>
    public bool TryResolveTitle(string candidate, out string title) =>
        Titles.TryGetValue(Key(candidate), out title!);

    /// <summary>
    /// Loads the embedded lexicon, then merges the user's file if present.
    /// User entries win on conflict.
    /// </summary>
    public static Lexicon Load(string? userLexiconPath = null)
    {
        var lexicon = new Lexicon();

        using (var stream = Assembly.GetExecutingAssembly()
                   .GetManifestResourceStream("HumbleRename.Data.lexicon.txt"))
        {
            if (stream is not null)
            {
                using var reader = new StreamReader(stream);
                lexicon.Merge(reader);
            }
        }

        var userPath = userLexiconPath ?? DefaultUserLexiconPath();
        if (!string.IsNullOrWhiteSpace(userPath) && File.Exists(userPath))
        {
            using var reader = new StreamReader(userPath);
            lexicon.Merge(reader);
        }

        return lexicon;
    }

    /// <summary>%APPDATA%\hbrename\lexicon.txt — created by the user, never by us.</summary>
    public static string DefaultUserLexiconPath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "hbrename",
            "lexicon.txt");

    private void Merge(TextReader reader)
    {
        var section = string.Empty;

        while (reader.ReadLine() is { } raw)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim().ToLowerInvariant();
                continue;
            }

            switch (section)
            {
                case "titles":
                    AddPair(line, static (self, k, v) => self.Titles[Key(k)] = v, this);
                    break;
                case "editions":
                    AddPair(line, static (self, k, v) => self.Editions[Key(k)] = v, this);
                    break;
                case "words":
                    Words.Add(line.ToLowerInvariant());
                    break;
                case "uppercase":
                    Uppercase.Add(line.ToLowerInvariant());
                    break;
                case "smallwords":
                    SmallWords.Add(line.ToLowerInvariant());
                    break;
                case "junk":
                    Junk.Add(line.ToLowerInvariant());
                    break;
            }
        }
    }

    private static void AddPair(string line, Action<Lexicon, string, string> assign, Lexicon target)
    {
        var equals = line.IndexOf('=');
        if (equals <= 0)
        {
            return;
        }

        var key = line[..equals].Trim();
        var value = line[(equals + 1)..].Trim();
        if (key.Length == 0 || value.Length == 0)
        {
            return;
        }

        assign(target, key, value);
    }
}
