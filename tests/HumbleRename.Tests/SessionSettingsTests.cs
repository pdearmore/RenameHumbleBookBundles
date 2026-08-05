using HumbleRename.Cli;

namespace HumbleRename.Tests;

public class SessionSettingsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "humblerenamer-settings-" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // A leftover temp file is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RoundTripsEverySetting()
    {
        var settings = new SessionSettings
        {
            Folder = @"D:\Comics\Bundle",
            TemplateIndex = 2,
            CustomTemplate = "{Title} - {Author}",
            FileTypeIndex = 1,
            CustomExtensions = ["cbz", "pdf"],
            Recurse = true,
            Online = true,
            ReadMetadata = false,
            HydrateCloudFiles = true,
        };

        settings.Save(_path);
        var loaded = SessionSettings.Load(_path);

        Assert.Equal(settings.Folder, loaded.Folder);
        Assert.Equal(settings.TemplateIndex, loaded.TemplateIndex);
        Assert.Equal(settings.CustomTemplate, loaded.CustomTemplate);
        Assert.Equal(settings.FileTypeIndex, loaded.FileTypeIndex);
        Assert.Equal(settings.CustomExtensions, loaded.CustomExtensions);
        Assert.Equal(settings.Recurse, loaded.Recurse);
        Assert.Equal(settings.Online, loaded.Online);
        Assert.Equal(settings.ReadMetadata, loaded.ReadMetadata);
        Assert.Equal(settings.HydrateCloudFiles, loaded.HydrateCloudFiles);
    }

    [Fact]
    public void MissingFileYieldsDefaults()
    {
        var loaded = SessionSettings.Load(_path); // never written

        Assert.Null(loaded.Folder);
        Assert.True(loaded.ReadMetadata); // metadata reading is on by default
        Assert.False(loaded.Online);
    }
}
