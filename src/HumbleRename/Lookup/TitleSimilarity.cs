using System.Text;

namespace HumbleRename.Lookup;

/// <summary>
/// Scores how well a catalogue result matches what we asked for.
/// </summary>
/// <remarks>
/// The interesting case is a truncated query. Calibre clips titles mid-word, so
/// "Star Wars Omnibus Rise of the S" should match "Star Wars Omnibus: Rise of the Sith"
/// with high confidence — a plain word-overlap score would rate that mediocre because
/// the final token differs. Prefix matching is therefore weighted separately.
/// </remarks>
public static class TitleSimilarity
{
    /// <summary>Reduces a title to lowercase alphanumerics separated by single spaces.</summary>
    public static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0)
                {
                    builder.Append(' ');
                }

                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                pendingSpace = true;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Plain similarity of two titles in [0,1], with no author signal involved.
    /// </summary>
    /// <remarks>
    /// Used to decide whether a file's embedded metadata is even describing the same
    /// work as its filename. PDF producers leave things like "Print" or
    /// "AHE Final Text" in the title field, and applying those would rename a
    /// correctly named file after a different book.
    /// </remarks>
    public static double Compare(string left, string right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return 1.0;
        }

        if (b.StartsWith(a, StringComparison.Ordinal) || a.StartsWith(b, StringComparison.Ordinal))
        {
            var shorter = Math.Min(a.Length, b.Length);
            var longer = Math.Max(a.Length, b.Length);
            return 0.75 + (0.20 * ((double)shorter / longer));
        }

        return DiceCoefficient(a, b);
    }

    /// <summary>
    /// Returns a confidence in [0,1] that <paramref name="candidate"/> is the work
    /// described by <paramref name="query"/>.
    /// </summary>
    public static double Score(LookupQuery query, LookupResult candidate)
    {
        var wanted = Normalize(query.Title);
        var found = Normalize(candidate.Title);

        if (wanted.Length == 0 || found.Length == 0)
        {
            return 0;
        }

        if (string.Equals(wanted, found, StringComparison.Ordinal))
        {
            return ApplyAuthorBonus(1.0, query, candidate);
        }

        double score;

        if (query.Truncated)
        {
            // Drop the final, probably-severed word before comparing prefixes.
            var stem = TrimLastWord(wanted);
            if (stem.Length >= 6 && found.StartsWith(stem, StringComparison.Ordinal))
            {
                // A long shared prefix is very strong evidence; scale with how much
                // of the candidate the prefix accounts for so a generic short stem
                // does not match every book in the catalogue.
                var coverage = (double)stem.Length / found.Length;
                score = 0.80 + (0.15 * Math.Min(1.0, coverage));
            }
            else
            {
                score = DiceCoefficient(wanted, found) * 0.85;
            }
        }
        else if (found.StartsWith(wanted, StringComparison.Ordinal) ||
                 wanted.StartsWith(found, StringComparison.Ordinal))
        {
            var shorter = Math.Min(wanted.Length, found.Length);
            var longer = Math.Max(wanted.Length, found.Length);
            score = 0.75 + (0.20 * ((double)shorter / longer));
        }
        else
        {
            score = DiceCoefficient(wanted, found);
        }

        return ApplyAuthorBonus(score, query, candidate);
    }

    private static double ApplyAuthorBonus(double score, LookupQuery query, LookupResult candidate)
    {
        if (string.IsNullOrWhiteSpace(query.Author) || string.IsNullOrWhiteSpace(candidate.Author))
        {
            return Math.Clamp(score, 0, 1);
        }

        var wanted = Normalize(query.Author);
        var found = Normalize(candidate.Author);

        // Surname agreement is enough — catalogues disagree wildly on initials.
        var wantedParts = wanted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var foundParts = found.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var shared = wantedParts.Length > 0 && foundParts.Length > 0 &&
                     wantedParts.Intersect(foundParts, StringComparer.Ordinal)
                         .Any(static p => p.Length > 2);

        return Math.Clamp(shared ? score + 0.10 : score - 0.05, 0, 1);
    }

    private static string TrimLastWord(string normalized)
    {
        var lastSpace = normalized.LastIndexOf(' ');
        return lastSpace <= 0 ? normalized : normalized[..lastSpace];
    }

    /// <summary>Dice coefficient over character bigrams — tolerant of word-order noise.</summary>
    private static double DiceCoefficient(string left, string right)
    {
        var leftPairs = Bigrams(left);
        var rightPairs = Bigrams(right);

        if (leftPairs.Count == 0 || rightPairs.Count == 0)
        {
            return 0;
        }

        var matches = 0;
        var consumed = new List<string>(rightPairs);
        foreach (var pair in leftPairs)
        {
            var index = consumed.IndexOf(pair);
            if (index >= 0)
            {
                consumed.RemoveAt(index);
                matches++;
            }
        }

        return 2.0 * matches / (leftPairs.Count + rightPairs.Count);
    }

    private static List<string> Bigrams(string value)
    {
        var pairs = new List<string>(Math.Max(0, value.Length - 1));
        for (var i = 0; i < value.Length - 1; i++)
        {
            if (value[i] != ' ' && value[i + 1] != ' ')
            {
                pairs.Add(value.Substring(i, 2));
            }
        }

        return pairs;
    }
}
