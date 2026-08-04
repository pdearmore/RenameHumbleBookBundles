using System.IO.Compression;
using System.Reflection;

namespace HumbleRename.Naming;

/// <summary>
/// Wires the lexicon, unigram corpus, segmenter, caser and parser together.
/// Construction reads a ~600 KB embedded corpus, so build one and reuse it.
/// </summary>
public sealed class NamingEngine
{
    public Lexicon Lexicon { get; }

    public WordSegmenter Segmenter { get; }

    public TitleCaser Caser { get; }

    public FilenameParser Parser { get; }

    private NamingEngine(Lexicon lexicon, WordSegmenter segmenter, TitleCaser caser, FilenameParser parser)
    {
        Lexicon = lexicon;
        Segmenter = segmenter;
        Caser = caser;
        Parser = parser;
    }

    /// <summary>
    /// Builds the engine, optionally merging a user lexicon over the built-in one.
    /// </summary>
    public static NamingEngine Create(string? userLexiconPath = null)
    {
        var lexicon = Lexicon.Load(userLexiconPath);
        var corpus = LoadCorpus();
        var segmenter = new WordSegmenter(corpus, lexicon.Words);
        var caser = new TitleCaser(lexicon, segmenter);
        var parser = new FilenameParser(lexicon, segmenter, caser);
        return new NamingEngine(lexicon, segmenter, caser, parser);
    }

    private static Dictionary<string, long> LoadCorpus()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("HumbleRename.Data.words.txt.gz")
            ?? throw new InvalidOperationException(
                "The embedded word corpus is missing. Rebuild the project so Data/words.txt.gz is packed in.");

        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        return WordSegmenter.ReadCorpus(gzip);
    }
}
