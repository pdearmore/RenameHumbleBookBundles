using System.Text;
using System.Text.RegularExpressions;
using HumbleRename.Model;
using UglyToad.PdfPig;

namespace HumbleRename.Metadata;

/// <summary>
/// Reads the document information dictionary from a PDF, falling back to a raw scan
/// for an XMP <c>dc:title</c> when the dictionary is absent or the file will not parse.
/// </summary>
public static partial class PdfMetadataReader
{
    /// <summary>How much of the file the XMP fallback will scan.</summary>
    private const int XmpScanLimit = 4 * 1024 * 1024;

    [GeneratedRegex(@"<dc:title>\s*(?:<rdf:Alt[^>]*>\s*<rdf:li[^>]*>)?(?<title>[^<]{2,300})",
        RegexOptions.IgnoreCase)]
    private static partial Regex XmpTitle();

    [GeneratedRegex(@"<dc:creator>\s*(?:<rdf:Seq[^>]*>\s*<rdf:li[^>]*>)?(?<creator>[^<]{2,200})",
        RegexOptions.IgnoreCase)]
    private static partial Regex XmpCreator();

    public static BookMetadata? Read(string path)
    {
        var fromDictionary = ReadDocumentInformation(path);
        if (fromDictionary is not null)
        {
            return fromDictionary;
        }

        return ReadXmpFallback(path);
    }

    private static BookMetadata? ReadDocumentInformation(string path)
    {
        try
        {
            using var document = PdfDocument.Open(path, new ParsingOptions { UseLenientParsing = true });
            var info = document.Information;

            var title = Clean(info.Title);
            var author = Clean(info.Author);

            if (title is null && author is null)
            {
                return null;
            }

            return new BookMetadata
            {
                Title = title,
                Author = author,
                Summary = Clean(info.Subject),
                Year = ParseYear(info.CreationDate),
                Source = MetadataSource.Embedded,
            };
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Encrypted, damaged or exotic PDFs are common in bundles; the filename
            // and online lookup still have a shot.
            return null;
        }
    }

    private static BookMetadata? ReadXmpFallback(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var length = (int)Math.Min(stream.Length, XmpScanLimit);
            var buffer = new byte[length];
            stream.ReadExactly(buffer, 0, length);

            // XMP packets are stored as uncompressed UTF-8 inside the file.
            var text = Encoding.UTF8.GetString(buffer);

            var title = Clean(XmpTitle().Match(text) is { Success: true } m ? m.Groups["title"].Value : null);
            if (title is null)
            {
                return null;
            }

            var creator = Clean(XmpCreator().Match(text) is { Success: true } c
                ? c.Groups["creator"].Value
                : null);

            return new BookMetadata
            {
                Title = title,
                Author = creator,
                Source = MetadataSource.Embedded,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rejects the placeholder titles that PDF producers leave behind — an untitled
    /// export whose "title" is really a filename or a tool name is worse than nothing.
    /// </summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        if (text.Length < 2)
        {
            return null;
        }

        if (text.Equals("untitled", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("unknown", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Microsoft Word", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // "SomeBook.indd" or "final_v3.pdf" is a source filename, not a title.
        var extension = Path.GetExtension(text);
        if (extension.Length is > 1 and <= 5 && !extension.Any(char.IsWhiteSpace))
        {
            return null;
        }

        return text;
    }

    private static int? ParseYear(string? creationDate)
    {
        if (string.IsNullOrWhiteSpace(creationDate))
        {
            return null;
        }

        // PDF dates look like D:20140312120000-04'00'.
        var digits = new string(creationDate.Where(char.IsAsciiDigit).Take(4).ToArray());
        if (int.TryParse(digits, out var year) && year is >= 1800 and <= 2200)
        {
            return year;
        }

        return null;
    }
}
