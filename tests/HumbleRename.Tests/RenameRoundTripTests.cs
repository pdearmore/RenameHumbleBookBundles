using HumbleRename.Renaming;

namespace HumbleRename.Tests;

/// <summary>
/// Exercises the plan/apply/revert cycle against a real temporary folder.
/// </summary>
public class RenameRoundTripTests : IDisposable
{
    private readonly string _folder;

    public RenameRoundTripTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "humblerenamer-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }

        GC.SuppressFinalize(this);
    }

    private void CreateFiles(params string[] names)
    {
        foreach (var name in names)
        {
            File.WriteAllText(Path.Combine(_folder, name), "stub");
        }
    }

    private async Task<RenamePlan> PlanAsync()
    {
        // Metadata reading is off so these tests exercise filename handling only.
        var options = new RenameOptions { UseEmbeddedMetadata = false };
        var planner = new RenamePlanner(TestEngine.Current, options);
        return await planner.BuildAsync(_folder);
    }

    [Fact]
    public async Task PlanningDoesNotTouchTheDisk()
    {
        CreateFiles("warmother.cbz", "whitesand.cbz");

        var plan = await PlanAsync();

        Assert.Equal(2, plan.ChangeCount);
        Assert.True(File.Exists(Path.Combine(_folder, "warmother.cbz")));
        Assert.True(File.Exists(Path.Combine(_folder, "whitesand.cbz")));
    }

    [Fact]
    public async Task ApplyThenRevertRestoresEveryOriginalName()
    {
        string[] originals =
        [
            "warmother.cbz",
            "nowhereman_vol1.cbz",
            "x-omanowar2017_vol1.cbz",
            "FromHell_1409941126.cbz",
            "ptsdradio_vol1_ebook.cbz",
        ];

        CreateFiles(originals);

        var plan = await PlanAsync();
        var applied = RenameExecutor.Apply(plan);

        Assert.Equal(originals.Length, applied.Succeeded);
        Assert.Empty(applied.Failures);
        Assert.NotNull(applied.Undo);
        Assert.True(File.Exists(Path.Combine(_folder, "War Mother.cbz")));

        var reverted = RenameExecutor.Revert(applied.Undo);

        Assert.Equal(originals.Length, reverted.Succeeded);
        Assert.Empty(reverted.Failures);

        foreach (var name in originals)
        {
            Assert.True(File.Exists(Path.Combine(_folder, name)), $"{name} was not restored");
        }
    }

    [Fact]
    public async Task UndoLogSurvivesBeingWrittenAndReadBack()
    {
        CreateFiles("warmother.cbz");

        var plan = await PlanAsync();
        RenameExecutor.Apply(plan);

        var reloaded = UndoLog.Load(_folder);

        Assert.NotNull(reloaded);
        Assert.Single(reloaded.Entries);
        Assert.Equal("warmother.cbz", reloaded.Entries[0].From);
        Assert.Equal("War Mother.cbz", reloaded.Entries[0].To);
    }

    [Fact]
    public async Task TwoFilesResolvingToOneNameGetDistinctTargets()
    {
        // Both of these reduce to "War Mother"; the second must be suffixed.
        CreateFiles("warmother.cbz", "war_mother.cbz");

        var plan = await PlanAsync();
        var applied = RenameExecutor.Apply(plan);

        Assert.Equal(2, applied.Succeeded);
        Assert.True(File.Exists(Path.Combine(_folder, "War Mother.cbz")));
        Assert.True(File.Exists(Path.Combine(_folder, "War Mother (2).cbz")));
    }

    [Fact]
    public async Task AlreadyCorrectNamesAreLeftAlone()
    {
        CreateFiles("War Mother.cbz");

        var plan = await PlanAsync();

        Assert.Equal(0, plan.ChangeCount);
        Assert.Equal(1, plan.UnchangedCount);
    }

    [Fact]
    public async Task TheUndoLogItselfIsNeverRenamed()
    {
        CreateFiles("warmother.cbz");
        File.WriteAllText(Path.Combine(_folder, UndoLog.FileName), "{}");

        var plan = await PlanAsync();

        Assert.DoesNotContain(plan.Actions, a => a.OriginalName == UndoLog.FileName);
    }
}
