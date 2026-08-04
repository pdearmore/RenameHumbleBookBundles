using HumbleRename.Model;

namespace HumbleRename.Metadata;

/// <summary>
/// Routes a file to the right metadata reader based on its sniffed format.
/// </summary>
public sealed class MetadataExtractor
{
    /// <summary>
    /// Set on OneDrive/Dropbox files whose contents live in the cloud. Opening one
    /// forces a full download, which turns a quick scan into a very long one.
    /// </summary>
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;

    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;

    private readonly bool _hydrateCloudFiles;

    /// <param name="hydrateCloudFiles">
    /// When false (the default), cloud-only placeholder files are left alone and only
    /// their filenames are used. When true, they are downloaded so metadata can be read.
    /// </param>
    public MetadataExtractor(bool hydrateCloudFiles = false) => _hydrateCloudFiles = hydrateCloudFiles;

    /// <summary>
    /// Reads embedded metadata, or returns <c>null</c> when the file has none, cannot
    /// be read, or is a cloud placeholder we chose not to download.
    /// </summary>
    public BookMetadata? Read(string path, out FileFormat format, out bool skippedCloudFile)
    {
        format = FileFormat.Unknown;
        skippedCloudFile = false;

        if (!File.Exists(path))
        {
            return null;
        }

        if (!_hydrateCloudFiles && IsCloudPlaceholder(path))
        {
            skippedCloudFile = true;
            return null;
        }

        format = FormatSniffer.Detect(path);

        return format switch
        {
            FileFormat.Mobi => MobiMetadataReader.Read(path),
            FileFormat.Pdf => PdfMetadataReader.Read(path),
            _ when format.IsArchive() => ArchiveMetadataReader.Read(path),
            _ => null,
        };
    }

    /// <summary>True when the file's bytes are not present locally.</summary>
    public static bool IsCloudPlaceholder(string path)
    {
        try
        {
            var attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.Offline) != 0 ||
                   (attributes & RecallOnDataAccess) != 0 ||
                   (attributes & RecallOnOpen) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
