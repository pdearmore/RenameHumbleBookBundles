using System.Xml.Linq;
using HumbleRename.Model;
using SharpCompress.Archives;

namespace HumbleRename.Metadata;

/// <summary>
/// Pulls metadata out of archive-shaped books: <c>ComicInfo.xml</c> from CBZ/CBR/CB7
/// and the OPF package document from EPUB.
/// </summary>
public static class ArchiveMetadataReader
{
    /// <summary>Entries larger than this are certainly page images, not metadata.</summary>
    private const long MaxMetadataEntrySize = 4 * 1024 * 1024;

    public static BookMetadata? Read(string path)
    {
        try
        {
            using var archive = ArchiveFactory.OpenArchive(path);

            XDocument? comicInfo = null;
            XDocument? opf = null;

            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || entry.Key is null || entry.Size > MaxMetadataEntrySize)
                {
                    continue;
                }

                var name = Path.GetFileName(entry.Key.Replace('\\', '/'));

                if (comicInfo is null && name.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase))
                {
                    comicInfo = TryLoad(entry);
                }
                else if (opf is null && name.EndsWith(".opf", StringComparison.OrdinalIgnoreCase))
                {
                    opf = TryLoad(entry);
                }

                if (comicInfo is not null)
                {
                    // ComicInfo is the richer and more trustworthy of the two.
                    break;
                }
            }

            if (comicInfo is not null)
            {
                return ParseComicInfo(comicInfo);
            }

            return opf is not null ? ParseOpf(opf) : null;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException or NotSupportedException)
        {
            // Unreadable or unsupported archive — fall back to the filename.
            return null;
        }
    }

    private static XDocument? TryLoad(IArchiveEntry entry)
    {
        try
        {
            using var stream = entry.OpenEntryStream();
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return XDocument.Load(buffer);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Parses the ComicRack ComicInfo.xml schema.</summary>
    internal static BookMetadata? ParseComicInfo(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        var series = Value(root, "Series");
        var title = Value(root, "Title");
        var number = Value(root, "Number");

        if (series is null && title is null)
        {
            return null;
        }

        // ComicInfo's <Volume> is usually the series' start year, not a collected
        // volume number, so only treat small values as a volume.
        int? volume = null;
        if (int.TryParse(Value(root, "Volume"), out var volumeValue) && volumeValue is > 0 and < 1000)
        {
            volume = volumeValue;
        }

        int? year = null;
        if (int.TryParse(Value(root, "Year"), out var yearValue) && yearValue is >= 1800 and <= 2200)
        {
            year = yearValue;
        }

        return new BookMetadata
        {
            Series = series,
            Title = title ?? series,
            Subtitle = series is not null && title is not null && !title.Equals(series, StringComparison.OrdinalIgnoreCase)
                ? title
                : null,
            Issue = string.IsNullOrWhiteSpace(number) ? null : number,
            Volume = volume,
            Year = year,
            Author = Value(root, "Writer") ?? Value(root, "Penciller"),
            Publisher = Value(root, "Publisher"),
            Summary = Value(root, "Summary"),
            Source = MetadataSource.Embedded,
        };
    }

    /// <summary>Parses an EPUB OPF package document (Dublin Core plus Calibre extensions).</summary>
    internal static BookMetadata? ParseOpf(XDocument document)
    {
        var root = document.Root;
        if (root is null)
        {
            return null;
        }

        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        var metadata = root.Descendants().FirstOrDefault(e => e.Name.LocalName == "metadata") ?? root;

        var title = metadata.Elements(dc + "title").FirstOrDefault()?.Value.Trim()
                    ?? Descendant(metadata, "title");
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var creator = metadata.Elements(dc + "creator").FirstOrDefault()?.Value.Trim()
                      ?? Descendant(metadata, "creator");
        var publisher = metadata.Elements(dc + "publisher").FirstOrDefault()?.Value.Trim()
                        ?? Descendant(metadata, "publisher");
        var date = metadata.Elements(dc + "date").FirstOrDefault()?.Value.Trim()
                   ?? Descendant(metadata, "date");

        // Calibre stores series information in <meta name="calibre:series" content="...">.
        string? series = null;
        int? volume = null;
        foreach (var meta in metadata.Descendants().Where(static e => e.Name.LocalName == "meta"))
        {
            var name = meta.Attribute("name")?.Value;
            var content = meta.Attribute("content")?.Value;
            if (name is null || string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (name.Equals("calibre:series", StringComparison.OrdinalIgnoreCase))
            {
                series = content.Trim();
            }
            else if (name.Equals("calibre:series_index", StringComparison.OrdinalIgnoreCase) &&
                     double.TryParse(content, out var index) && index is > 0 and < 1000)
            {
                volume = (int)index;
            }
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(date) && DateTimeOffset.TryParse(date, out var parsed))
        {
            year = parsed.Year;
        }

        return new BookMetadata
        {
            Title = title.Trim(),
            Series = series,
            Author = string.IsNullOrWhiteSpace(creator) ? null : creator,
            Publisher = string.IsNullOrWhiteSpace(publisher) ? null : publisher,
            Volume = volume,
            Year = year,
            Source = MetadataSource.Embedded,
        };
    }

    private static string? Value(XElement root, string localName)
    {
        var element = root.Elements().FirstOrDefault(e =>
            e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

        var text = element?.Value.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string? Descendant(XElement root, string localName)
    {
        var element = root.Descendants().FirstOrDefault(e =>
            e.Name.LocalName.Equals(localName, StringComparison.OrdinalIgnoreCase));

        var text = element?.Value.Trim();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
