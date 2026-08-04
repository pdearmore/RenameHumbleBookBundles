using System.Text;
using System.Text.RegularExpressions;

namespace HumbleRename.Naming;

/// <summary>
/// Applies headline capitalisation: every word capitalised except articles,
/// coordinating conjunctions and short prepositions, which stay lowercase unless
/// they open or close the title (or open a subtitle).
/// </summary>
public sealed partial class TitleCaser
{
    private readonly Lexicon _lexicon;
    private readonly WordSegmenter? _segmenter;

    public TitleCaser(Lexicon lexicon, WordSegmenter? segmenter = null)
    {
        _lexicon = lexicon ?? throw new ArgumentNullException(nameof(lexicon));
        _segmenter = segmenter;
    }

    [GeneratedRegex(@"^(?=[mdclxvi]+$)m{0,4}(cm|cd|d?c{0,3})(xc|xl|l?x{0,3})(ix|iv|v?i{0,3})$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RomanNumeralPattern();

    /// <summary>Characters after which the next word starts a fresh clause and is always capitalised.</summary>
    private const string ClauseOpeners = ":;-–—([{\"'!?./";

    /// <summary>Internal separators whose halves are capitalised independently ("One-Shot").</summary>
    private static readonly char[] CompoundSeparators = ['-', '/', '–', '—'];

    public string ToTitleCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            return string.Empty;
        }

        var result = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
        {
            var isFirst = i == 0;
            var isLast = i == tokens.Length - 1;
            var opensClause = isFirst || StartsNewClause(tokens[i - 1]);
            result[i] = CaseToken(tokens[i], forceCapital: opensClause || isLast);
        }

        return string.Join(' ', result);
    }

    private static bool StartsNewClause(string previousToken)
    {
        if (previousToken.Length == 0)
        {
            return false;
        }

        var last = previousToken[^1];
        // A trailing period on an abbreviation ("vs.", "K.") does not open a clause,
        // but one closing a sentence fragment does. Treat single/double letter
        // abbreviations as non-openers.
        if (last == '.' && previousToken.Length <= 3)
        {
            return false;
        }

        return ClauseOpeners.Contains(last);
    }

    /// <summary>
    /// Cases one whitespace-delimited token, recursing through internal hyphens,
    /// slashes and apostrophes so "demi-human" becomes "Demi-Human" and
    /// "god's" becomes "God's" rather than "God'S".
    /// </summary>
    private string CaseToken(string token, bool forceCapital)
    {
        if (token.Length == 0)
        {
            return token;
        }

        // Peel leading/trailing punctuation so lookups see the bare word.
        var start = 0;
        var end = token.Length;
        while (start < end && !char.IsLetterOrDigit(token[start]))
        {
            start++;
        }

        while (end > start && !char.IsLetterOrDigit(token[end - 1]))
        {
            end--;
        }

        if (start >= end)
        {
            return token;
        }

        var prefix = token[..start];
        var core = token[start..end];
        var suffix = token[end..];

        return prefix + CaseCore(core, forceCapital) + suffix;
    }

    private string CaseCore(string core, bool forceCapital)
    {
        // Split on internal punctuation, casing each part independently.
        var separatorIndex = core.IndexOfAny(CompoundSeparators);
        if (separatorIndex > 0 && separatorIndex < core.Length - 1)
        {
            var separator = core[separatorIndex];
            var left = core[..separatorIndex];
            var right = core[(separatorIndex + 1)..];
            // Both halves of a hyphenated compound get capitals ("One-Shot", "X-O").
            return CaseCore(left, forceCapital: true) + separator + CaseCore(right, forceCapital: true);
        }

        var apostrophe = core.IndexOfAny(['\'', '’']);
        if (apostrophe > 0 && apostrophe < core.Length - 1)
        {
            var left = core[..apostrophe];
            var mark = core[apostrophe];
            var right = core[(apostrophe + 1)..];
            // "O'Brien" and "D'Artagnan" capitalise after the mark; "God's" does not.
            var capitalizeRight = left.Length == 1;
            return CaseCore(left, forceCapital) + mark +
                   (capitalizeRight ? CaseCore(right, forceCapital: true) : right.ToLowerInvariant());
        }

        return CaseWord(core, forceCapital);
    }

    private string CaseWord(string word, bool forceCapital)
    {
        var lower = word.ToLowerInvariant();

        // Explicit acronyms always win.
        if (_lexicon.Uppercase.Contains(lower))
        {
            return word.ToUpperInvariant();
        }

        // Anything containing a digit keeps its shape ("4001", "2017", "004").
        if (word.Any(char.IsAsciiDigit))
        {
            return word;
        }

        // An all-caps token that is not an ordinary English word is almost certainly
        // an acronym the user typed deliberately — leave it alone.
        if (word.Length is >= 2 and <= 5 && word.All(static c => !char.IsLetter(c) || char.IsUpper(c))
            && !IsDictionaryWord(lower))
        {
            return word.ToUpperInvariant();
        }

        // Roman numerals, but only when the letters do not also spell a real word.
        // Without that guard "mix" and "did" would be shouted as numerals.
        if (RomanNumeralPattern().IsMatch(lower) && !IsDictionaryWord(lower))
        {
            return word.ToUpperInvariant();
        }

        if (!forceCapital && _lexicon.SmallWords.Contains(lower))
        {
            return lower;
        }

        return Capitalize(lower);
    }

    private bool IsDictionaryWord(string lower) => _segmenter?.IsKnownWord(lower) ?? false;

    private static string Capitalize(string lower)
    {
        if (lower.Length == 0)
        {
            return lower;
        }

        var builder = new StringBuilder(lower.Length);
        builder.Append(char.ToUpperInvariant(lower[0]));
        builder.Append(lower, 1, lower.Length - 1);
        return builder.ToString();
    }
}
