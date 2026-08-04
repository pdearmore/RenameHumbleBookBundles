using HumbleRename.Naming;

namespace HumbleRename.Tests;

public class WordSegmenterTests
{
    private static string Segment(string input) => TestEngine.Current.Segmenter.SegmentToString(input);

    [Theory]
    [InlineData("chillingadventuresofsabrina", "chilling adventures of sabrina")]
    [InlineData("faithandthefutureforce", "faith and the future force")]
    [InlineData("imageexposampler", "image expo sampler")]
    [InlineData("oblivionsong", "oblivion song")]
    [InlineData("generationzero", "generation zero")]
    [InlineData("secretweapons", "secret weapons")]
    public void SplitsRunTogetherWords(string input, string expected) =>
        Assert.Equal(expected, Segment(input));

    [Fact]
    public void LeavesASingleWordAlone() =>
        Assert.Equal("britannia", Segment("britannia"));

    [Fact]
    public void RecognisesInjectedDomainVocabulary() =>
        // "manowar" is not English; it reaches the segmenter from the lexicon.
        Assert.True(TestEngine.Current.Segmenter.IsKnownWord("manowar"));

    [Fact]
    public void KnowsOrdinaryEnglishWords()
    {
        Assert.True(TestEngine.Current.Segmenter.IsKnownWord("mother"));
        Assert.False(TestEngine.Current.Segmenter.IsKnownWord("zzzqqxx"));
    }

    [Fact]
    public void EmptyInputProducesNoWords() =>
        Assert.Empty(TestEngine.Current.Segmenter.Segment("   "));

    [Fact]
    public void RejectsAnEmptyCorpus() =>
        Assert.Throws<ArgumentException>(() => new WordSegmenter(new Dictionary<string, long>()));

    [Fact]
    public void LongUnknownRunsAreNotSwallowedAsOneWord()
    {
        // The length penalty should force a split rather than accept nonsense whole.
        var words = TestEngine.Current.Segmenter.Segment("qwertyuiopasdfghjkl");

        Assert.True(words.Count > 1);
    }
}
