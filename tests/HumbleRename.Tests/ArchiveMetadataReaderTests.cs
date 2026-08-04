using System.Xml.Linq;
using HumbleRename.Metadata;

namespace HumbleRename.Tests;

public class ArchiveMetadataReaderTests
{
    [Fact]
    public void ReadsComicInfoFields()
    {
        var document = XDocument.Parse("""
            <?xml version="1.0"?>
            <ComicInfo>
              <Series>The Walking Dead</Series>
              <Title>Days Gone Bye</Title>
              <Number>1</Number>
              <Volume>1</Volume>
              <Year>2003</Year>
              <Writer>Robert Kirkman</Writer>
              <Publisher>Image Comics</Publisher>
            </ComicInfo>
            """);

        var metadata = ArchiveMetadataReader.ParseComicInfo(document);

        Assert.NotNull(metadata);
        Assert.Equal("The Walking Dead", metadata.Series);
        Assert.Equal("Days Gone Bye", metadata.Subtitle);
        Assert.Equal("1", metadata.Issue);
        Assert.Equal(2003, metadata.Year);
        Assert.Equal("Robert Kirkman", metadata.Author);
        Assert.Equal("Image Comics", metadata.Publisher);
    }

    [Fact]
    public void TreatsAYearInTheVolumeFieldAsNotAVolume()
    {
        // ComicInfo's <Volume> is frequently the series' start year, not a volume number.
        var document = XDocument.Parse(
            "<ComicInfo><Series>Saga</Series><Volume>2012</Volume></ComicInfo>");

        var metadata = ArchiveMetadataReader.ParseComicInfo(document);

        Assert.NotNull(metadata);
        Assert.Null(metadata.Volume);
    }

    [Fact]
    public void ReturnsNothingWhenComicInfoHasNoTitleOrSeries()
    {
        var document = XDocument.Parse("<ComicInfo><PageCount>22</PageCount></ComicInfo>");

        Assert.Null(ArchiveMetadataReader.ParseComicInfo(document));
    }

    [Fact]
    public void ReadsEpubPackageMetadata()
    {
        var document = XDocument.Parse("""
            <?xml version="1.0"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:title>Saga</dc:title>
                <dc:creator>Brian K. Vaughan</dc:creator>
                <dc:publisher>Image Comics</dc:publisher>
                <dc:date>2012-10-10</dc:date>
                <meta name="calibre:series" content="Saga" />
                <meta name="calibre:series_index" content="1" />
              </metadata>
            </package>
            """);

        var metadata = ArchiveMetadataReader.ParseOpf(document);

        Assert.NotNull(metadata);
        Assert.Equal("Saga", metadata.Title);
        Assert.Equal("Brian K. Vaughan", metadata.Author);
        Assert.Equal("Image Comics", metadata.Publisher);
        Assert.Equal(2012, metadata.Year);
        Assert.Equal(1, metadata.Volume);
    }

    [Fact]
    public void ReturnsNothingWhenOpfHasNoTitle()
    {
        var document = XDocument.Parse("""
            <package xmlns="http://www.idpf.org/2007/opf">
              <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
                <dc:creator>Nobody</dc:creator>
              </metadata>
            </package>
            """);

        Assert.Null(ArchiveMetadataReader.ParseOpf(document));
    }
}
