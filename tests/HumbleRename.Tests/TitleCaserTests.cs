namespace HumbleRename.Tests;

public class TitleCaserTests
{
    private static string Case(string input) => TestEngine.Current.Caser.ToTitleCase(input);

    [Theory]
    [InlineData("the call of the stars", "The Call of the Stars")]
    [InlineData("chilling adventures of sabrina", "Chilling Adventures of Sabrina")]
    [InlineData("faith and the future force", "Faith and the Future Force")]
    [InlineData("in the dark", "In the Dark")]
    public void LowercasesSmallWordsInsideTheTitle(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Theory]
    // A small word still takes a capital when it opens or closes the title.
    [InlineData("the fuse", "The Fuse")]
    [InlineData("a train called love", "A Train Called Love")]
    [InlineData("what are you waiting for", "What Are You Waiting For")]
    public void CapitalisesFirstAndLastWord(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Theory]
    [InlineData("demi-human", "Demi-Human")]
    [InlineData("one-shot", "One-Shot")]
    [InlineData("x-o manowar", "X-O Manowar")]
    public void CapitalisesBothHalvesOfACompound(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Theory]
    [InlineData("god's redemption", "God's Redemption")]
    [InlineData("poe's snifter of terror", "Poe's Snifter of Terror")]
    // A single letter before the apostrophe is a name prefix, not a possessive.
    [InlineData("o'brien", "O'Brien")]
    public void HandlesApostrophes(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Theory]
    [InlineData("ptsd radio", "PTSD Radio")]
    [InlineData("mad magazine", "MAD Magazine")]
    public void UppercasesKnownAcronyms(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Theory]
    // Letters that spell a real word must not be shouted as roman numerals.
    [InlineData("mix", "Mix")]
    [InlineData("did", "Did")]
    [InlineData("civil", "Civil")]
    [InlineData("dim", "Dim")]
    public void DoesNotMistakeOrdinaryWordsForRomanNumerals(string input, string expected) =>
        Assert.Equal(expected, Case(input));

    [Fact]
    public void CapitalisesAWordThatOpensASubtitle() =>
        Assert.Equal("Divinity: The Complete Trilogy", Case("divinity: the complete trilogy"));

    [Fact]
    public void LeavesNumbersAlone() =>
        Assert.Equal("30 Days of Night", Case("30 days of night"));

    [Fact]
    public void EmptyInputReturnsEmpty() =>
        Assert.Equal(string.Empty, Case("   "));
}
