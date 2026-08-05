using System.Buffers.Binary;
using System.Text;
using HumbleRename.Model;

namespace HumbleRename.Metadata;

/// <summary>
/// Reads EXTH metadata out of MOBI/AZW3 files.
/// </summary>
/// <remarks>
/// Humble's Calibre-produced .mobi files carry the complete title in EXTH record 503
/// even when the filename itself was clipped to ~30 characters, so this reader is
/// usually the difference between "The Action Bible: God's Redempt" and the real title.
/// Layout reference: https://wiki.mobileread.com/wiki/MOBI
/// </remarks>
public static class MobiMetadataReader
{
    private const int PalmRecordInfoStart = 78;
    private const int PalmRecordInfoSize = 8;

    // EXTH record types we care about.
    private const uint ExthAuthor = 100;
    private const uint ExthPublisher = 101;
    private const uint ExthDescription = 103;
    private const uint ExthIsbn = 104;
    private const uint ExthPublishDate = 106;
    private const uint ExthUpdatedTitle = 503;

    public static BookMetadata? Read(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return Parse(bytes);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static BookMetadata? Parse(byte[] bytes)
    {
        if (bytes.Length < 96)
        {
            return null;
        }

        var recordCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(76, 2));
        if (recordCount == 0 || PalmRecordInfoStart + PalmRecordInfoSize > bytes.Length)
        {
            return null;
        }

        var record0 = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(PalmRecordInfoStart, 4));
        if (record0 <= 0 || record0 + 232 > bytes.Length)
        {
            return null;
        }

        var encoding = ResolveEncoding(bytes, record0);

        // The MOBI header follows the 16-byte PalmDOC header; EXTH follows the MOBI header.
        var exthOffset = -1;
        if (Encoding.ASCII.GetString(bytes, record0 + 16, 4) == "MOBI")
        {
            var mobiHeaderLength = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record0 + 20, 4));
            var exthFlags = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record0 + 128, 4));
            var candidate = record0 + 16 + mobiHeaderLength;

            if ((exthFlags & 0x40) != 0 && candidate + 12 <= bytes.Length &&
                Encoding.ASCII.GetString(bytes, candidate, 4) == "EXTH")
            {
                exthOffset = candidate;
            }
        }

        // Some producers write a header length that does not line up. Fall back to
        // scanning the front of the file for the marker.
        exthOffset = exthOffset >= 0 ? exthOffset : FindExth(bytes);

        string? title = null, author = null, publisher = null, isbn = null, summary = null, published = null;

        if (exthOffset >= 0)
        {
            foreach (var (type, data) in ReadExthRecords(bytes, exthOffset))
            {
                var value = encoding.GetString(data).Trim();
                if (value.Length == 0)
                {
                    continue;
                }

                switch (type)
                {
                    case ExthUpdatedTitle: title ??= value; break;
                    case ExthAuthor: author ??= value; break;
                    case ExthPublisher: publisher ??= value; break;
                    case ExthIsbn: isbn ??= value; break;
                    case ExthDescription: summary ??= value; break;
                    case ExthPublishDate: published ??= value; break;
                }
            }
        }

        title ??= ReadFullName(bytes, record0, encoding);

        if (title is null && author is null && publisher is null)
        {
            return null;
        }

        return new BookMetadata
        {
            Title = Clean(title),
            Author = Clean(author),
            Publisher = Clean(publisher),
            Isbn = string.IsNullOrWhiteSpace(isbn) ? null : isbn,
            Summary = Clean(summary),
            Year = ParseYear(published),
            Source = MetadataSource.Embedded,
        };
    }

    /// <summary>Walks the EXTH record table, yielding each record's type and payload.</summary>
    private static IEnumerable<(uint Type, byte[] Data)> ReadExthRecords(byte[] bytes, int exthOffset)
    {
        var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(exthOffset + 8, 4));
        var cursor = exthOffset + 12;

        for (var i = 0; i < count; i++)
        {
            if (cursor + 8 > bytes.Length)
            {
                yield break;
            }

            var type = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor, 4));
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(cursor + 4, 4));

            // A malformed length would walk us off the end or spin forever.
            if (length < 8 || cursor + length > bytes.Length)
            {
                yield break;
            }

            yield return (type, bytes[(cursor + 8)..(cursor + length)]);
            cursor += length;
        }
    }

    /// <summary>Reads the "full name" field the MOBI header points at.</summary>
    private static string? ReadFullName(byte[] bytes, int record0, Encoding encoding)
    {
        if (record0 + 92 > bytes.Length)
        {
            return null;
        }

        var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record0 + 84, 4));
        var length = (int)BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record0 + 88, 4));
        var start = record0 + offset;

        if (length <= 0 || length > 1024 || start < 0 || start + length > bytes.Length)
        {
            return null;
        }

        return encoding.GetString(bytes, start, length).Trim('\0', ' ');
    }

    private static Encoding ResolveEncoding(byte[] bytes, int record0)
    {
        var codepage = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(record0 + 28, 4));
        return codepage == 65001 ? Encoding.UTF8 : Encoding.Latin1;
    }

    private static int FindExth(byte[] bytes)
    {
        ReadOnlySpan<byte> marker = "EXTH"u8;
        var limit = Math.Min(bytes.Length - marker.Length, 1 << 17);

        for (var i = 0; i < limit; i++)
        {
            if (bytes.AsSpan(i, marker.Length).SequenceEqual(marker))
            {
                return i;
            }
        }

        return -1;
    }

    private static int? ParseYear(string? published)
    {
        if (string.IsNullOrWhiteSpace(published))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(published, out var parsed))
        {
            return parsed.Year;
        }

        // Fall back to the first plausible 4-digit run.
        for (var i = 0; i + 4 <= published.Length; i++)
        {
            var slice = published.AsSpan(i, 4);
            if (int.TryParse(slice, out var year) && year is >= 1800 and <= 2200)
            {
                return year;
            }
        }

        return null;
    }

    /// <summary>Strips the HTML that publishers routinely dump into description fields.</summary>
    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);
        var insideTag = false;
        foreach (var c in value)
        {
            switch (c)
            {
                case '<': insideTag = true; continue;
                case '>': insideTag = false; continue;
            }

            if (!insideTag)
            {
                builder.Append(char.IsControl(c) ? ' ' : c);
            }
        }

        var text = builder.ToString().Trim();
        return text.Length == 0 ? null : text;
    }
}
