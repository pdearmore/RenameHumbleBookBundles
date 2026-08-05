using System.Text;

namespace HumbleRename.Metadata;

/// <summary>Container formats HumbleRenamer can look inside.</summary>
public enum FileFormat
{
    Unknown = 0,
    Zip,
    Rar,
    SevenZip,
    Tar,
    Pdf,
    Mobi,
    Epub,
}

/// <summary>
/// Identifies a file by its leading bytes rather than its extension.
/// </summary>
/// <remarks>
/// Extensions lie constantly in comic collections: plenty of files named ".cbr" are
/// really ZIPs, and ".cbz" files are sometimes RARs. Sniffing avoids handing the wrong
/// decoder a file and reporting a bogus "corrupt archive".
/// </remarks>
public static class FormatSniffer
{
    /// <summary>Bytes needed to identify every supported format (MOBI's marker sits at 60).</summary>
    private const int HeaderLength = 272;

    public static FileFormat Detect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return Detect(stream);
        }
        catch (IOException)
        {
            return FileFormat.Unknown;
        }
        catch (UnauthorizedAccessException)
        {
            return FileFormat.Unknown;
        }
    }

    public static FileFormat Detect(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderLength];
        var read = ReadAtLeast(stream, header);
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        if (read >= 4 && Matches(header, 0, [0x25, 0x50, 0x44, 0x46]))
        {
            return FileFormat.Pdf;
        }

        if (read >= 7 && Matches(header, 0, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07]))
        {
            return FileFormat.Rar;
        }

        if (read >= 6 && Matches(header, 0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C]))
        {
            return FileFormat.SevenZip;
        }

        // PalmDB (MOBI/AZW3) puts its type+creator at offset 60, not at the start;
        // offset 0 holds the book's internal name, so the file looks like plain text.
        if (read >= 68)
        {
            var palmType = Encoding.ASCII.GetString(header, 60, 8);
            if (palmType is "BOOKMOBI" or "TEXtREAd")
            {
                return FileFormat.Mobi;
            }
        }

        if (read >= 4 && header[0] == 0x50 && header[1] == 0x4B &&
            (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07))
        {
            // An EPUB is a ZIP whose first entry is an uncompressed "mimetype" file.
            if (read >= 58 && Encoding.ASCII.GetString(header, 30, 28)
                    .StartsWith("mimetypeapplication/epub+zip", StringComparison.Ordinal))
            {
                return FileFormat.Epub;
            }

            return FileFormat.Zip;
        }

        if (read >= 262 && Encoding.ASCII.GetString(header, 257, 5) == "ustar")
        {
            return FileFormat.Tar;
        }

        return FileFormat.Unknown;
    }

    /// <summary>True for formats that hold entries worth searching for metadata.</summary>
    public static bool IsArchive(this FileFormat format) =>
        format is FileFormat.Zip or FileFormat.Rar or FileFormat.SevenZip
            or FileFormat.Tar or FileFormat.Epub;

    private static int ReadAtLeast(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool Matches(byte[] buffer, int offset, ReadOnlySpan<byte> signature)
    {
        if (offset + signature.Length > buffer.Length)
        {
            return false;
        }

        for (var i = 0; i < signature.Length; i++)
        {
            if (buffer[offset + i] != signature[i])
            {
                return false;
            }
        }

        return true;
    }
}
