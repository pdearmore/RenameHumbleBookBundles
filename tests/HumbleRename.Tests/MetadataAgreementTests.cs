using HumbleRename.Lookup;

namespace HumbleRename.Tests;

/// <summary>
/// Guards the rule that stops a file's embedded metadata overwriting a good filename
/// with a production artefact.
/// </summary>
/// <remarks>
/// Real cases from a Humble PDF bundle: one file's <c>/Title</c> was literally
/// "Print", and another's was "Neverwhere AHE Final Text" on a file called
/// "neverwear_aportfolioofstories" — a different book entirely.
/// </remarks>
public class MetadataAgreementTests
{
    /// <summary>Mirrors RenamePlanner.MinimumMetadataAgreement.</summary>
    private const double Threshold = 0.45;

    [Theory]
    [InlineData("Neil Gaiman: Book Bundle: Free Tier", "Print")]
    [InlineData("Neverwear: A Portfolio of Stories", "Neverwhere AHE Final Text")]
    [InlineData("Signal to Noise", "Untitled-1")]
    [InlineData("A Calendar of Tales", "Document1")]
    // A short fragment turning up inside an unrelated title proves nothing.
    [InlineData("Art", "The Art of Starting Over: A Memoir")]
    public void RejectsMetadataThatDisagreesWithTheFilename(string fromFilename, string fromMetadata) =>
        Assert.True(TitleSimilarity.Compare(fromFilename, fromMetadata) < Threshold,
            $"expected disagreement, scored {TitleSimilarity.Compare(fromFilename, fromMetadata):F2}");

    [Theory]
    // The metadata adding a subtitle the filename omitted is the normal, good case.
    [InlineData("Angels and Visitations", "Angels and Visitations: A Miscellany")]
    [InlineData("The Action Bible: God's Redempt", "The Action Bible")]
    [InlineData("Day of the Dead", "Day of the Dead")]
    [InlineData("Nailbiter", "Nailbiter Vol. 1")]
    // The filename is an abbreviation sitting inside the real title, not a mismatch.
    [InlineData("Stitch Dictionary", "Crochet Every Way Stitch Dictionary: 125 Essential Stitches")]
    public void AcceptsMetadataThatAgrees(string fromFilename, string fromMetadata) =>
        Assert.True(TitleSimilarity.Compare(fromFilename, fromMetadata) >= Threshold,
            $"expected agreement, scored {TitleSimilarity.Compare(fromFilename, fromMetadata):F2}");

    [Fact]
    public void ComparisonIsSymmetric() =>
        Assert.Equal(
            TitleSimilarity.Compare("Signal to Noise", "Signal to Noise Revised"),
            TitleSimilarity.Compare("Signal to Noise Revised", "Signal to Noise"),
            precision: 6);

    [Fact]
    public void EmptyTitlesDoNotAgree() =>
        Assert.Equal(0, TitleSimilarity.Compare("", "Anything"));
}
