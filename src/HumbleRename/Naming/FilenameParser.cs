using System.Text;
using System.Text.RegularExpressions;
using HumbleRename.Model;

namespace HumbleRename.Naming;

/// <summary>
/// Turns a Humble Bundle style filename into structured bibliographic fields.
/// </summary>
/// <remarks>
/// Humble's DRM-free downloads arrive lowercased and run together
/// ("chillingadventuresofsabrina_vol1"), while Calibre exports arrive as
/// "Title - Author" with the title truncated to ~30 characters. Both shapes, plus
/// scene-release naming, are handled here.
/// </remarks>
public sealed partial class FilenameParser
{
    /// <summary>
    /// Marks segment boundaries while separators are rewritten. A control character
    /// cannot occur in a Windows filename, so it never collides with real text.
    /// </summary>
    private const char SegmentSeparator = (char)1;

    private readonly Lexicon _lexicon;
    private readonly WordSegmenter _segmenter;
    private readonly TitleCaser _caser;

    public FilenameParser(Lexicon lexicon, WordSegmenter segmenter, TitleCaser caser)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
        _segmenter = segmenter ?? throw new ArgumentNullException(nameof(segmenter));
        _caser = caser ?? throw new ArgumentNullException(nameof(caser));
    }

    [GeneratedRegex(@"^(?:vol|volume|v)\.?\s*0*(\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneVolume();

    [GeneratedRegex(@"^(?:issue|no|num)\.?\s*0*(\d{1,4})$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneIssue();

    [GeneratedRegex(@"^book\.?\s*0*(\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex StandaloneBook();

    [GeneratedRegex(@"^#\s*0*(\d{1,4})$")]
    private static partial Regex HashIssue();

    /// <summary>
    /// Trailing volume glued to a run-together name with no separator:
    /// "lockeandkeyv1", "butcherbakertherighteousmakervol1", "nailbitervolume2".
    /// Recognising the "vol"/"volume" spellings (not just a bare "v") is what lets the
    /// title lexicon fire on the stem — otherwise the volume stays welded on and the
    /// whole name falls through to the word splitter.
    /// </summary>
    [GeneratedRegex(@"^(?<name>.*[a-z])(?:volume|vol|v)\.?0*(?<vol>\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex GluedVolume();

    /// <summary>Trailing series year glued to a name: "quantumandwoody2017".</summary>
    [GeneratedRegex(@"^(?<name>.*[a-z])(?<year>(?:19|20)\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex GluedYear();

    /// <summary>
    /// A download id run straight onto the title with no separator:
    /// "anhonestanswerandotherstories1442260106".
    /// </summary>
    [GeneratedRegex(@"^(?<name>.*[a-z])(?<id>\d{9,14})$", RegexOptions.IgnoreCase)]
    private static partial Regex GluedAssetId();

    /// <summary>Humble/Calibre numeric asset ids and bare ISBNs.</summary>
    [GeneratedRegex(@"^\d{9,14}$")]
    private static partial Regex AssetId();

    /// <summary>Windows duplicate-file marker: "something (2)".</summary>
    [GeneratedRegex(@"^(?<name>.+\S)\s*\(\d{1,2}\)$")]
    private static partial Regex CopySuffix();

    [GeneratedRegex(@"^(?:19|20)\d{2}$")]
    private static partial Regex YearOnly();

    /// <summary>Trailing issue number on an otherwise spaced name: "Snifter of Terror 004".</summary>
    [GeneratedRegex(@"^(?<name>.+?)\s+(?<issue>\d{1,4})$")]
    private static partial Regex TrailingIssue();

    /// <summary>Trailing volume inside a spaced name: "Nailbiter Vol. 1", "Satellite Sam, Vol. 1".</summary>
    [GeneratedRegex(@"^(?<name>.+?)[,\s]\s*(?:vol|volume|v)\.?\s*0*(?<vol>\d{1,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingVolume();

    /// <summary>Trailing hashed issue inside a spaced name: "Transformers vs. The Terminator #1".</summary>
    [GeneratedRegex(@"^(?<name>.+?)\s*#\s*0*(?<issue>\d{1,4})$")]
    private static partial Regex TrailingHashIssue();

    /// <summary>
    /// Parses <paramref name="fileNameWithoutExtension"/> into metadata.
    /// <paramref name="knownAuthor"/>, when supplied from embedded metadata, lets the
    /// parser recognise a trailing "- Author" with confidence.
    /// </summary>
    public BookMetadata Parse(string fileNameWithoutExtension, string? knownAuthor = null)
    {
        if (string.IsNullOrWhiteSpace(fileNameWithoutExtension))
        {
            return new BookMetadata { Source = MetadataSource.Filename };
        }

        var working = fileNameWithoutExtension.Trim();

        // 0. Windows appends " (2)" when a file lands beside one of the same name.
        //    That is a filesystem artefact, not part of the title.
        if (CopySuffix().Match(working) is { Success: true } copy)
        {
            working = copy.Groups["name"].Value.Trim();
        }

        // 1. Pull out parentheticals: keep years, discard scene tags.
        working = ExtractParentheticals(working, out var parenYear);

        // 2. Split a trailing "- Author" (Calibre) from a trailing "- Subtitle" (scene).
        working = ExtractAuthorSuffix(working, knownAuthor, out var author);

        // 3. "Bible_ God's" is a colon Windows would not allow; "vol_1" is a separator.
        working = NormalizeSeparators(working);

        var segments = working
            .Split(SegmentSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Select(static s => s.Trim())
            .Where(static s => s.Length > 0)
            .ToList();

        var content = new List<string>();
        var editions = new List<string>();
        int? volume = null, book = null, year = parenYear, seriesYear = null;
        string? issue = null;
        string? isbn = null;

        foreach (var segment in segments)
        {
            var token = segment.Trim();
            if (token.Length == 0)
            {
                continue;
            }

            var key = Lexicon.Key(token);

            if (AssetId().IsMatch(token))
            {
                // 13-digit runs beginning 97 are ISBNs worth keeping; the rest are download ids.
                if (token.Length == 13 && token.StartsWith("97", StringComparison.Ordinal))
                {
                    isbn = token;
                }

                continue;
            }

            if (_lexicon.Junk.Contains(key))
            {
                continue;
            }

            if (_lexicon.Editions.TryGetValue(key, out var edition))
            {
                editions.Add(edition);
                continue;
            }

            if (StandaloneVolume().Match(token) is { Success: true } vm)
            {
                volume = int.Parse(vm.Groups[1].Value);
                continue;
            }

            if (StandaloneBook().Match(token) is { Success: true } bm)
            {
                book = int.Parse(bm.Groups[1].Value);
                continue;
            }

            if (StandaloneIssue().Match(token) is { Success: true } im)
            {
                issue = im.Groups[1].Value;
                continue;
            }

            if (HashIssue().Match(token) is { Success: true } hm)
            {
                issue = hm.Groups[1].Value;
                continue;
            }

            if (YearOnly().IsMatch(token))
            {
                year ??= int.Parse(token);
                continue;
            }

            content.Add(token);
        }

        // 4. A run-together name may still carry a glued volume or series year.
        for (var i = 0; i < content.Count; i++)
        {
            var token = content[i];

            // Strip a glued download id before anything else, or its trailing four
            // digits get mistaken for a year.
            if (GluedAssetId().Match(token) is { Success: true } ga)
            {
                var stem = ga.Groups["name"].Value;
                if (stem.Length >= 4)
                {
                    var id = ga.Groups["id"].Value;
                    if (id.Length == 13 && id.StartsWith("97", StringComparison.Ordinal))
                    {
                        isbn ??= id;
                    }

                    content[i] = stem;
                    token = stem;
                }
            }

            if (!token.Contains(' ') && GluedVolume().Match(token) is { Success: true } gv)
            {
                var stem = gv.Groups["name"].Value;
                if (stem.Length >= 3)
                {
                    volume ??= int.Parse(gv.Groups["vol"].Value);
                    content[i] = stem;
                    token = stem;
                }
            }

            if (GluedYear().Match(token) is { Success: true } gy)
            {
                var stem = gy.Groups["name"].Value;
                if (stem.Length >= 3)
                {
                    seriesYear ??= int.Parse(gy.Groups["year"].Value);
                    content[i] = stem;
                }
            }
        }

        // 5. Resolve each remaining segment to real words.
        var resolved = ResolveContent(content, editions);

        // 6. A spaced name may still trail a volume, a hashed issue, or a bare number.
        //    This runs after resolution so edition markers are already gone — otherwise
        //    the trailing "TP" in "Satellite Sam, Vol. 1 TP" hides the volume. Order
        //    matters too: "Vol. 1" must be claimed as a volume before the bare-number
        //    rule reads the 1 as an issue.
        if (resolved.Count > 0)
        {
            var last = resolved[^1];

            if (TrailingHashIssue().Match(last) is { Success: true } hashMatch)
            {
                var stem = hashMatch.Groups["name"].Value.Trim();
                if (stem.Length >= 3)
                {
                    issue ??= hashMatch.Groups["issue"].Value;
                    resolved[^1] = stem;
                    last = stem;
                }
            }

            if (volume is null && TrailingVolume().Match(last) is { Success: true } volMatch)
            {
                var stem = volMatch.Groups["name"].Value.Trim();
                if (stem.Length >= 3)
                {
                    volume = int.Parse(volMatch.Groups["vol"].Value);
                    resolved[^1] = stem;
                    last = stem;
                }
            }

            if (issue is null && volume is null && last.Contains(' ') &&
                TrailingIssue().Match(last) is { Success: true } tm)
            {
                var stem = tm.Groups["name"].Value.Trim();
                var candidate = tm.Groups["issue"].Value;
                // Four digits at the end is far more likely a year than an issue.
                if (stem.Length >= 3 && !YearOnly().IsMatch(candidate))
                {
                    issue = candidate;
                    resolved[^1] = stem;
                }
            }

            resolved[^1] = resolved[^1].Trim(' ', ',', '-', ':', ';');
        }

        // 7. The volume often sits on the series rather than the last segment:
        //    "americangodsvolume1_shadows" is American Gods vol 1, subtitled Shadows.
        if (volume is null && resolved.Count > 1 &&
            TrailingVolume().Match(resolved[0]) is { Success: true } seriesVolume)
        {
            var stem = seriesVolume.Groups["name"].Value.Trim();
            if (stem.Length >= 3)
            {
                volume = int.Parse(seriesVolume.Groups["vol"].Value);
                resolved[0] = stem;
            }
        }

        var series = resolved.Count > 0 ? resolved[0] : string.Empty;
        var subtitle = resolved.Count > 1 ? string.Join(": ", resolved.Skip(1)) : null;

        // A trailing repeat of the title ("MAD Magazine #4 - MAD") carries nothing.
        if (author is not null && series.Length > 0 &&
            series.Contains(author, StringComparison.OrdinalIgnoreCase))
        {
            author = null;
        }

        return new BookMetadata
        {
            Series = series.Length > 0 ? series : null,
            Subtitle = subtitle,
            Title = BuildPlainTitle(series, subtitle),
            Author = author,
            Volume = volume,
            Issue = issue,
            Book = book,
            Year = year ?? seriesYear,
            Isbn = isbn,
            Editions = editions,
            Source = MetadataSource.Filename,
            LooksTruncated = LooksTruncated(series, subtitle),
        };
    }

    private static string? BuildPlainTitle(string series, string? subtitle)
    {
        if (series.Length == 0)
        {
            return null;
        }

        return string.IsNullOrEmpty(subtitle) ? series : $"{series}: {subtitle}";
    }

    /// <summary>
    /// Removes bracketed groups, keeping a 4-digit year and discarding scene tags
    /// like "(digital)" or "(The Magicians-Empire)".
    /// </summary>
    private string ExtractParentheticals(string input, out int? year)
    {
        year = null;
        var builder = new StringBuilder(input.Length);
        var depth = 0;
        var current = new StringBuilder();

        foreach (var c in input)
        {
            if (c is '(' or '[')
            {
                depth++;
                if (depth == 1)
                {
                    current.Clear();
                    continue;
                }
            }
            else if (c is ')' or ']')
            {
                if (depth == 1)
                {
                    depth = 0;
                    var inner = current.ToString().Trim();
                    if (YearOnly().IsMatch(inner))
                    {
                        year ??= int.Parse(inner);
                    }
                    else if (!IsSceneTag(inner))
                    {
                        builder.Append(' ').Append(inner).Append(' ');
                    }

                    continue;
                }

                depth = Math.Max(0, depth - 1);
            }

            if (depth > 0)
            {
                current.Append(c);
            }
            else
            {
                builder.Append(c);
            }
        }

        if (depth > 0)
        {
            builder.Append(current);
        }

        return CollapseWhitespace(builder.ToString());
    }

    /// <summary>
    /// True for release-group and format noise. Scene groups usually appear as
    /// "Something-Empire", and single junk words like "digital" are listed outright.
    /// </summary>
    private bool IsSceneTag(string inner)
    {
        if (inner.Length == 0)
        {
            return true;
        }

        if (_lexicon.Junk.Contains(Lexicon.Key(inner)))
        {
            return true;
        }

        foreach (var part in inner.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (_lexicon.Junk.Contains(Lexicon.Key(part)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Splits a trailing " - X". X is treated as an author when it matches known
    /// metadata, is the literal "Unknown", or reads like a personal name.
    /// </summary>
    private static string ExtractAuthorSuffix(string input, string? knownAuthor, out string? author)
    {
        author = null;
        var index = input.LastIndexOf(" - ", StringComparison.Ordinal);
        if (index <= 0)
        {
            return input;
        }

        var head = input[..index].Trim();
        var tail = input[(index + 3)..].Trim();
        if (tail.Length == 0 || head.Length == 0)
        {
            return input;
        }

        if (tail.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return head;
        }

        if (!string.IsNullOrWhiteSpace(knownAuthor) &&
            tail.Equals(knownAuthor.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            author = knownAuthor.Trim();
            return head;
        }

        // "Brian K. Vaughan" reads as an author; "Hunters" is a subtitle.
        var words = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var looksLikePerson = words.Length is >= 2 and <= 4 &&
                              words.All(static w => w.Length > 0 && char.IsUpper(w[0])) &&
                              !tail.Any(char.IsAsciiDigit);

        if (looksLikePerson)
        {
            author = tail;
            return head;
        }

        // Single trailing word that merely repeats part of the title is dead weight.
        if (words.Length == 1 && head.Contains(tail, StringComparison.OrdinalIgnoreCase))
        {
            return head;
        }

        return input;
    }

    /// <summary>
    /// Rewrites separators into a sentinel so segments split unambiguously.
    /// "_ " was almost certainly a colon that Windows forbade in a filename.
    /// </summary>
    private static string NormalizeSeparators(string input)
    {
        var builder = new StringBuilder(input.Length);
        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];
            if (c == '_')
            {
                // "Bible_ God's" -> a colon that could not be written to disk.
                if (i + 1 < input.Length && input[i + 1] == ' ')
                {
                    builder.Append(SegmentSeparator);
                    i++;
                    continue;
                }

                builder.Append(SegmentSeparator);
                continue;
            }

            if (c == ':')
            {
                builder.Append(SegmentSeparator);
                continue;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Converts raw segments into properly spaced, properly cased text, pulling out
    /// any edition phrases that only became visible after word segmentation.
    /// </summary>
    private List<string> ResolveContent(List<string> content, List<string> editions)
    {
        // A single lexicon entry may span every segment ("ajin" + "demihuman").
        if (content.Count > 0)
        {
            var wholeKey = string.Concat(content.Select(Lexicon.Key));
            if (_lexicon.Titles.TryGetValue(wholeKey, out var whole))
            {
                return [whole];
            }
        }

        var resolved = new List<string>(content.Count);
        foreach (var segment in content)
        {
            var text = ResolveSegment(segment);
            text = ExtractEditionPhrases(text, editions);
            if (!string.IsNullOrWhiteSpace(text))
            {
                resolved.Add(text);
            }
        }

        return resolved;
    }

    private string ResolveSegment(string segment)
    {
        if (_lexicon.TryResolveTitle(segment, out var known))
        {
            return known;
        }

        // Already spaced (a scene or Calibre name) — only the casing needs work.
        if (segment.Contains(' '))
        {
            return _caser.ToTitleCase(CollapseWhitespace(segment));
        }

        if (TryExpandAuthorPrefix(segment, out var withAuthor))
        {
            return withAuthor;
        }

        return _caser.ToTitleCase(SegmentRunTogether(segment));
    }

    /// <summary>
    /// Peels a known author's name off the front of a run-together token.
    /// </summary>
    /// <remarks>
    /// Humble builds bundles around one author and glues the name to every filename.
    /// The hard part is the possessive: "neilgaimanstrollbridge" is "Neil Gaiman's
    /// Troll Bridge", but a word-frequency splitter happily reads the orphaned 's'
    /// as the start of the next word and returns "Neil Gaiman Stroll Bridge". Since
    /// author bundles are named possessively, that reading wins unless the lexicon
    /// recognises the non-possessive remainder instead.
    /// </remarks>
    private bool TryExpandAuthorPrefix(string segment, out string expanded)
    {
        expanded = string.Empty;

        var key = Lexicon.Key(segment);
        if (key.Length == 0)
        {
            return false;
        }

        foreach (var (authorKey, authorName) in _lexicon.AuthorsByLength)
        {
            if (authorKey.Length == 0 || !key.StartsWith(authorKey, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = key[authorKey.Length..];
            if (rest.Length == 0)
            {
                expanded = authorName;
                return true;
            }

            var possessive = false;
            if (rest[0] == 's')
            {
                var stripped = rest[1..];

                // Whichever remainder the lexicon knows is the right reading; absent
                // that, assume the possessive.
                if (_lexicon.Titles.ContainsKey(stripped))
                {
                    rest = stripped;
                    possessive = true;
                }
                else if (!_lexicon.Titles.ContainsKey(rest))
                {
                    rest = stripped;
                    possessive = true;
                }
            }

            if (rest.Length < 3)
            {
                return false;
            }

            var title = ResolveSegment(rest);
            expanded = possessive ? $"{authorName}'s {title}" : $"{authorName} {title}";
            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits a run-together token, preserving hyphens and digit boundaries the
    /// segmenter would otherwise swallow ("30daysofnight" keeps its 30).
    /// </summary>
    private string SegmentRunTogether(string token)
    {
        var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
        var rendered = new List<string>(parts.Length);

        foreach (var part in parts)
        {
            if (_lexicon.TryResolveTitle(part, out var knownPart))
            {
                rendered.Add(knownPart);
                continue;
            }

            var builder = new StringBuilder();
            foreach (var (text, literal) in SplitRuns(part.ToLowerInvariant()))
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                // An initialism has to survive intact; the splitter would shred
                // "fcbd" into "fc bd" given the chance.
                var keepWhole = literal || _segmenter.IsLikelyAcronym(text);
                builder.Append(keepWhole ? text : _segmenter.SegmentToString(text));
            }

            rendered.Add(builder.ToString());
        }

        return string.Join('-', rendered);
    }

    /// <summary>
    /// Splits into letter and digit runs so "30daysofnight" keeps its 30, then glues
    /// ordinal suffixes back onto their number.
    /// </summary>
    /// <remarks>
    /// Without the second step "2ndedition" splits to "2" + "ndedition" and comes out
    /// as "2 Nd Edition". Runs flagged <c>Literal</c> bypass the word splitter, which
    /// would otherwise shred "2nd" right back apart.
    /// </remarks>
    private static IEnumerable<(string Text, bool Literal)> SplitRuns(string value)
    {
        var runs = SplitLetterDigitRuns(value);

        for (var i = 0; i < runs.Count; i++)
        {
            var run = runs[i];
            var isDigits = run.Length > 0 && run.All(char.IsAsciiDigit);

            if (isDigits && i + 1 < runs.Count && runs[i + 1].Length >= 2)
            {
                var suffix = runs[i + 1][..2];
                if (IsOrdinalSuffixFor(run, suffix))
                {
                    yield return (run + suffix, true);

                    var remainder = runs[i + 1][2..];
                    if (remainder.Length > 0)
                    {
                        yield return (remainder, false);
                    }

                    i++;
                    continue;
                }
            }

            yield return (run, isDigits);
        }
    }

    /// <summary>Checks that "st", "nd", "rd" or "th" is the correct suffix for a number.</summary>
    private static bool IsOrdinalSuffixFor(string digits, string suffix)
    {
        if (!int.TryParse(digits, out var number))
        {
            return false;
        }

        var lastTwo = number % 100;
        var last = number % 10;

        // 11th, 12th and 13th break the otherwise simple last-digit rule.
        if (lastTwo is 11 or 12 or 13)
        {
            return suffix == "th";
        }

        return suffix switch
        {
            "st" => last == 1,
            "nd" => last == 2,
            "rd" => last == 3,
            "th" => last is 0 or 4 or 5 or 6 or 7 or 8 or 9,
            _ => false,
        };
    }

    /// <summary>Yields alternating letter and digit runs.</summary>
    private static List<string> SplitLetterDigitRuns(string value)
    {
        var runs = new List<string>();
        if (value.Length == 0)
        {
            return runs;
        }

        var start = 0;
        for (var i = 1; i <= value.Length; i++)
        {
            var boundary = i == value.Length ||
                           char.IsAsciiDigit(value[i]) != char.IsAsciiDigit(value[start]);
            if (!boundary)
            {
                continue;
            }

            runs.Add(value[start..i]);
            start = i;
        }

        return runs;
    }

    /// <summary>
    /// Finds edition phrases inside already-segmented text ("army of darkness one shot")
    /// and lifts them out into <paramref name="editions"/>.
    /// </summary>
    private string ExtractEditionPhrases(string text, List<string> editions)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Only the leading or trailing edge is considered. A marker in the middle is
        // part of the title itself — "Star Wars Omnibus Rise of the Sith" must not
        // lose its "Omnibus" and become "Star Wars Rise of the Sith".
        var changed = true;
        while (changed && words.Count > 1)
        {
            changed = false;

            // Longest match first so "deluxe edition" beats "deluxe".
            for (var length = Math.Min(3, words.Count - 1); length >= 1 && !changed; length--)
            {
                var suffixKey = Lexicon.Key(string.Concat(words.Skip(words.Count - length)));
                if (_lexicon.Editions.TryGetValue(suffixKey, out var suffixEdition))
                {
                    AddEdition(editions, suffixEdition);
                    words.RemoveRange(words.Count - length, length);
                    changed = true;
                    break;
                }

                var prefixKey = Lexicon.Key(string.Concat(words.Take(length)));
                if (_lexicon.Editions.TryGetValue(prefixKey, out var prefixEdition))
                {
                    AddEdition(editions, prefixEdition);
                    words.RemoveRange(0, length);
                    changed = true;
                }
            }
        }

        return string.Join(' ', words).Trim(' ', ',', '-', ':', ';');

        static void AddEdition(List<string> target, string edition)
        {
            if (!target.Contains(edition))
            {
                target.Add(edition);
            }
        }
    }

    /// <summary>
    /// Detects exporter truncation: Calibre clips titles to roughly 30 characters,
    /// leaving a dangling fragment like "Rise of the S" or "God's Redempt".
    /// </summary>
    private bool LooksTruncated(string series, string? subtitle)
    {
        var text = string.IsNullOrEmpty(subtitle) ? series : subtitle;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // A title the lexicon supplied verbatim is complete by definition, however
        // unfamiliar its last word looks. "Here's Negan" ends in a character name the
        // corpus has never seen, but it is not clipped.
        if (_lexicon.Titles.ContainsKey(Lexicon.Key(text)))
        {
            return false;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return false;
        }

        var lastToken = words[^1].Trim('.', ',', '!', '?', '"', '\'', ')', '(');

        // Judge the final element of a compound: "Demi-Human" hinges on "Human".
        var hyphen = lastToken.LastIndexOf('-');
        if (hyphen >= 0 && hyphen < lastToken.Length - 1)
        {
            lastToken = lastToken[(hyphen + 1)..];
        }

        var last = lastToken.ToLowerInvariant();
        if (last.Length == 0)
        {
            return false;
        }

        // A dangling single letter is the clearest tell.
        if (last.Length == 1 && last is not "a" and not "i")
        {
            return true;
        }

        // Otherwise: an unrecognised final word that is not a known proper noun.
        // An initialism such as FCBD is complete, not a clipped word.
        var recognised = _segmenter.IsKnownWord(last) ||
                         _lexicon.Words.Contains(last) ||
                         _lexicon.Uppercase.Contains(last) ||
                         _segmenter.IsLikelyAcronym(last) ||
                         IsKnownCompound(last) ||
                         last.Any(char.IsAsciiDigit);

        return !recognised && last.Length >= 4;
    }

    /// <summary>
    /// True when an unrecognised word is really a compound of recognised ones.
    /// </summary>
    /// <remarks>
    /// The corpus holds the 80,000 most frequent words, so perfectly ordinary
    /// compounds fall outside it — "ragdolls", "dishcloths". Those were being reported
    /// as clipped titles. A genuine truncation does not decompose this way:
    /// "redempt" leaves a fragment behind, while "dishcloths" is cleanly dish + cloths.
    /// </remarks>
    private bool IsKnownCompound(string word)
    {
        if (word.Length < 6)
        {
            return false;
        }

        var parts = _segmenter.Segment(word);
        if (parts.Count < 2)
        {
            return false;
        }

        foreach (var part in parts)
        {
            // Every piece must be everyday vocabulary in its own right. Mere corpus
            // membership is too weak: "redempt" splits into "red" and "empt", and
            // "empt" is present only as a rank-78,000 curiosity.
            if (part.Length < 3 || !_segmenter.IsFrequentWord(part))
            {
                return false;
            }
        }

        return true;
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
