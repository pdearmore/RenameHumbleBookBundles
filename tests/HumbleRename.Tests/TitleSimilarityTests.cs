using HumbleRename.Lookup;

namespace HumbleRename.Tests;

public class TitleSimilarityTests
{
    private static LookupResult Candidate(string title, string? author = null) =>
        new() { Title = title, Author = author, Provider = "test" };

    [Fact]
    public void IdenticalTitlesScorePerfectly()
    {
        var score = TitleSimilarity.Score(
            new LookupQuery("Nailbiter"),
            Candidate("Nailbiter"));

        Assert.Equal(1.0, score, precision: 3);
    }

    [Fact]
    public void TruncatedQueryMatchesItsCompletion()
    {
        // The whole point of the online lookup: Calibre clipped this title mid-word.
        var query = new LookupQuery("Star Wars Omnibus Rise of the S", Truncated: true);
        var score = TitleSimilarity.Score(query, Candidate("Star Wars Omnibus: Rise of the Sith"));

        Assert.True(score >= LookupService.DefaultMinimumConfidence,
            $"expected a confident match, got {score:F2}");
    }

    [Fact]
    public void TruncatedQueryDoesNotMatchAnUnrelatedBook()
    {
        var query = new LookupQuery("Star Wars Omnibus Rise of the S", Truncated: true);
        var score = TitleSimilarity.Score(query, Candidate("Cooking with Cast Iron"));

        Assert.True(score < LookupService.DefaultMinimumConfidence,
            $"expected a rejection, got {score:F2}");
    }

    [Fact]
    public void MatchingAuthorRaisesConfidence()
    {
        var withAuthor = TitleSimilarity.Score(
            new LookupQuery("Saga", "Brian K. Vaughan"),
            Candidate("Saga Volume One", "Brian K. Vaughan"));

        var withoutAuthor = TitleSimilarity.Score(
            new LookupQuery("Saga", "Brian K. Vaughan"),
            Candidate("Saga Volume One", "Someone Else"));

        Assert.True(withAuthor > withoutAuthor);
    }

    [Theory]
    [InlineData("The Walking Dead", "the walking dead")]
    [InlineData("X-O Manowar", "x o manowar")]
    [InlineData("God's Redemption", "god s redemption")]
    public void NormalisationStripsCaseAndPunctuation(string input, string expected) =>
        Assert.Equal(expected, TitleSimilarity.Normalize(input));

    [Fact]
    public void EmptyTitlesScoreZero() =>
        Assert.Equal(0, TitleSimilarity.Score(new LookupQuery(""), Candidate("Anything")));
}
