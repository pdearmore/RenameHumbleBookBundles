using HumbleRename.Model;
using HumbleRename.Naming;
using HumbleRename.Renaming;

namespace HumbleRename.Tests;

/// <summary>
/// One shared <see cref="NamingEngine"/> for the whole test run.
/// </summary>
/// <remarks>
/// Building an engine decompresses and indexes an 80,000-word corpus. Doing that per
/// test class would dominate the run time, and the engine is immutable once built.
/// </remarks>
internal static class TestEngine
{
    private static readonly Lazy<NamingEngine> Instance =
        new(() => NamingEngine.Create(userLexiconPath: NonExistentPath), isThreadSafe: true);

    /// <summary>
    /// Deliberately points nowhere so a lexicon in the developer's own %APPDATA%
    /// cannot change what the tests assert.
    /// </summary>
    private const string NonExistentPath = @"Z:\hbrename-tests\no-such-lexicon.txt";

    public static NamingEngine Current => Instance.Value;

    /// <summary>Parses a filename stem into metadata.</summary>
    public static BookMetadata Parse(string stem) => Current.Parser.Parse(stem);

    /// <summary>
    /// Runs the full naming pipeline exactly as the tool does, returning the final
    /// on-disk name minus the extension.
    /// </summary>
    public static string FinalName(string stem, string? template = null)
    {
        var metadata = Current.Parser.Parse(stem);
        var rendered = NameTemplate.Render(template ?? NameTemplate.Default, metadata);
        return PathSafety.MakeSafeFileName(rendered);
    }
}
