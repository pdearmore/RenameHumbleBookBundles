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

    [Theory]
    [InlineData("fcbd")]
    [InlineData("bprd")]
    public void TreatsShortVowelFreeRunsAsInitialisms(string word) =>
        Assert.True(TestEngine.Current.Segmenter.IsLikelyAcronym(word));

    [Theory]
    [InlineData("dead")]      // has vowels
    [InlineData("a")]         // too short
    [InlineData("strengths")] // too long
    [InlineData("why")]       // a real word, however vowel-free
    // "tpb" reaches the corpus as a real token, so the known-word guard excludes it.
    // That is the desired outcome: it is an edition marker, handled by the lexicon.
    [InlineData("tpb")]
    public void DoesNotMistakeOrdinaryWordsForInitialisms(string word) =>
        Assert.False(TestEngine.Current.Segmenter.IsLikelyAcronym(word));

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
