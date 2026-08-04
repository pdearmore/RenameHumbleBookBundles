using HumbleRename.Model;
using HumbleRename.Renaming;

namespace HumbleRename.Tests;

public class NameTemplateTests
{
    private static readonly BookMetadata Full = new()
    {
        Series = "The Walking Dead",
        Subtitle = "Days Gone Bye",
        Title = "The Walking Dead: Days Gone Bye",
        Volume = 1,
        Year = 2003,
        Author = "Robert Kirkman",
        Editions = ["Deluxe Edition"],
    };

    [Fact]
    public void RendersEveryPopulatedSection() =>
        Assert.Equal(
            "The Walking Dead Vol. 01 - Days Gone Bye (2003) (Deluxe Edition)",
            NameTemplate.Render(NameTemplate.Default, Full));

    [Fact]
    public void DropsBracketedSectionsWhoseTokensAreEmpty()
    {
        var sparse = new BookMetadata { Series = "Britannia", Title = "Britannia" };

        Assert.Equal("Britannia", NameTemplate.Render(NameTemplate.Default, sparse));
    }

    [Fact]
    public void PadsVolumeToTheRequestedWidth()
    {
        var metadata = new BookMetadata { Series = "Saga", Volume = 7 };

        Assert.Equal("Saga v07", NameTemplate.Render("{Series}[ v{Volume:00}]", metadata));
    }

    [Fact]
    public void KeepsIssueTextSoLeadingZerosSurvive()
    {
        var metadata = new BookMetadata { Series = "Snifter of Terror", Issue = "004" };

        Assert.Equal("Snifter of Terror #004", NameTemplate.Render("{Series}[ #{Issue}]", metadata));
    }

    [Fact]
    public void IssueZeroIsRenderedNotTreatedAsMissing()
    {
        var metadata = new BookMetadata { Series = "Red Sonja", Issue = "0" };

        Assert.Equal("Red Sonja #0", NameTemplate.Render("{Series}[ #{Issue}]", metadata));
    }

    [Fact]
    public void JoinsMultipleEditions()
    {
        var metadata = new BookMetadata
        {
            Series = "Army of Darkness",
            Editions = ["Humble Exclusive", "One-Shot"],
        };

        Assert.Equal(
            "Army of Darkness (Humble Exclusive, One-Shot)",
            NameTemplate.Render("{Series}[ ({Editions})]", metadata));
    }

    [Fact]
    public void FallsBackToSeriesWhenTitleIsAbsent()
    {
        var metadata = new BookMetadata { Series = "Rapture" };

        Assert.Equal("Rapture", NameTemplate.Render("{Title}", metadata));
    }

    [Fact]
    public void EmptyTemplateFallsBackToTheDefault() =>
        Assert.Equal(
            NameTemplate.Render(NameTemplate.Default, Full),
            NameTemplate.Render("   ", Full));

    [Fact]
    public void CustomTemplateIsHonoured() =>
        Assert.Equal(
            "Robert Kirkman - The Walking Dead (2003)",
            NameTemplate.Render("{Author} - {Series}[ ({Year})]", Full));
}
