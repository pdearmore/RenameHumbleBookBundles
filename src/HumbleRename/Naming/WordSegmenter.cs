using System.Text;

namespace HumbleRename.Naming;

/// <summary>
/// Splits run-together lowercase text ("chillingadventuresofsabrina") back into
/// words using a unigram language model and Viterbi search.
/// </summary>
/// <remarks>
/// This is Norvig's segmentation algorithm (Beautiful Data, ch. 14). Each candidate
/// word scores as log10(count / total); unknown runs are charged a penalty that grows
/// with length, so the search will not paper over garbage by calling it one long word.
/// Scores are summed rather than multiplied to stay clear of floating-point underflow.
/// </remarks>
public sealed class WordSegmenter
{
    /// <summary>
    /// Longest substring considered as a single word. Bounds the search at O(n * MaxWordLength).
    /// </summary>
    public const int MaxWordLength = 28;

    private readonly Dictionary<string, double> _logProbability;
    private readonly double _logTotal;

    /// <summary>
    /// Log-probability floor separating everyday vocabulary from the long tail.
    /// Membership alone is a weak signal: "empt" sits at rank 78,399 in the corpus,
    /// so a mere presence test would vouch for the fragment left by a clipped word.
    /// </summary>
    private readonly double _frequentWordFloor;

    /// <summary>Corpus rank at which a word stops counting as everyday vocabulary.</summary>
    private const int FrequentWordRank = 40000;

    /// <summary>Corpus rank whose weight is given to injected domain vocabulary.</summary>
    private const int BoostRank = 15000;

    /// <summary>
    /// Small bonus subtracted per word boundary. Discourages shredding a name into
    /// many tiny common words ("t he ninj ettes") when fewer longer words fit as well.
    /// </summary>
    private const double WordBoundaryPenalty = 0.25;

    public WordSegmenter(IReadOnlyDictionary<string, long> counts, IEnumerable<string>? boostedWords = null)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Count == 0)
        {
            throw new ArgumentException("The unigram corpus is empty.", nameof(counts));
        }

        double total = 0;
        foreach (var count in counts.Values)
        {
            total += count;
        }

        _logTotal = Math.Log10(total);
        _logProbability = new Dictionary<string, double>(counts.Count, StringComparer.Ordinal);
        foreach (var (word, count) in counts)
        {
            _logProbability[word] = Math.Log10(count) - _logTotal;
        }

        // One sort serves both derived thresholds.
        var ordered = counts.Values.OrderByDescending(static c => c).ToArray();
        _frequentWordFloor =
            Math.Log10(Math.Max(ordered[Math.Min(FrequentWordRank, ordered.Length - 1)], 1)) - _logTotal;

        // Domain vocabulary (character and publisher names) never appears in a general
        // English corpus. Give each entry the weight of a genuine but uncommon word so it
        // can win against an English split without steamrolling ordinary prose.
        var boostWeight = Math.Max(ordered[Math.Min(BoostRank, ordered.Length - 1)], 1);
        foreach (var word in boostedWords ?? [])
        {
            var normalized = word.Trim().ToLowerInvariant();
            if (normalized.Length == 0)
            {
                continue;
            }

            var boosted = Math.Log10(boostWeight) - _logTotal;
            if (!_logProbability.TryGetValue(normalized, out var existing) || existing < boosted)
            {
                _logProbability[normalized] = boosted;
            }
        }
    }

    /// <summary>True when the corpus recognises <paramref name="word"/> at all.</summary>
    public bool IsKnownWord(string word) =>
        !string.IsNullOrEmpty(word) && _logProbability.ContainsKey(word.ToLowerInvariant());

    /// <summary>
    /// True when <paramref name="word"/> is everyday vocabulary rather than a long-tail
    /// entry. Use this wherever a weak match would be worse than no match.
    /// </summary>
    public bool IsFrequentWord(string word) =>
        !string.IsNullOrEmpty(word) &&
        _logProbability.TryGetValue(word.ToLowerInvariant(), out var probability) &&
        probability >= _frequentWordFloor;

    /// <summary>
    /// True for a short run of letters with no vowel in it, which in a filename is
    /// almost always an initialism rather than words.
    /// </summary>
    /// <remarks>
    /// Without this, "fcbd" (Free Comic Book Day) is happily split into "fc bd" and
    /// title-cased to "Fc Bd". English has essentially no vowel-free words at this
    /// length, so the rule is safe; anything the corpus does recognise is excluded
    /// regardless.
    /// </remarks>
    public bool IsLikelyAcronym(string word)
    {
        if (string.IsNullOrEmpty(word) || word.Length is < 2 or > 5)
        {
            return false;
        }

        foreach (var c in word)
        {
            if (!char.IsAsciiLetter(c) || "aeiou".Contains(char.ToLowerInvariant(c)))
            {
                return false;
            }
        }

        return !IsKnownWord(word);
    }

    /// <summary>
    /// Segments a single run of letters. Input should already be lowercased and
    /// stripped of separators; anything non-alphanumeric is returned untouched.
    /// </summary>
    public IReadOnlyList<string> Segment(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var n = text.Length;

        // best[i] = score of the best segmentation of the first i characters.
        var best = new double[n + 1];
        var backtrack = new int[n + 1];
        best[0] = 0;
        for (var i = 1; i <= n; i++)
        {
            best[i] = double.NegativeInfinity;
            backtrack[i] = i - 1;
        }

        for (var end = 1; end <= n; end++)
        {
            var earliest = Math.Max(0, end - MaxWordLength);
            for (var start = earliest; start < end; start++)
            {
                if (double.IsNegativeInfinity(best[start]))
                {
                    continue;
                }

                var candidate = text[start..end];
                var score = best[start] + ScoreWord(candidate) - WordBoundaryPenalty;
                if (score > best[end])
                {
                    best[end] = score;
                    backtrack[end] = start;
                }
            }
        }

        var words = new List<string>();
        var cursor = n;
        while (cursor > 0)
        {
            var start = backtrack[cursor];
            words.Add(text[start..cursor]);
            cursor = start;
        }

        words.Reverse();
        return words;
    }

    /// <summary>Segments text and joins the result with single spaces.</summary>
    public string SegmentToString(string text) => string.Join(' ', Segment(text));

    private double ScoreWord(string word)
    {
        if (_logProbability.TryGetValue(word, out var known))
        {
            return known;
        }

        // A digit run is a perfectly good token — don't penalise it by length.
        if (IsAllDigits(word))
        {
            return -3.0 - (0.1 * word.Length);
        }

        // Unknown: plausible only if short. Each extra character costs another decade
        // of probability, which is what stops the search returning one giant "word".
        return 1.0 - _logTotal - (word.Length * 1.6);
    }

    private static bool IsAllDigits(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    /// <summary>
    /// Reads a "word&lt;TAB&gt;count" corpus, optionally gzipped, into a lookup table.
    /// </summary>
    public static Dictionary<string, long> ReadCorpus(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var tab = line.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            var word = line[..tab];
            if (!long.TryParse(line[(tab + 1)..], out var count) || count <= 0)
            {
                continue;
            }

            counts[word] = count;
        }

        return counts;
    }
}
