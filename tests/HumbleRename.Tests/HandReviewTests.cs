using HumbleRename.Renaming;

namespace HumbleRename.Tests;

/// <summary>
/// Covers the data behind hand-review: the per-file candidate list the planner keeps,
/// and rebuilding a plan from the names a user picked. The console loop that drives
/// these is thin presentation over what is asserted here.
/// </summary>
public class HandReviewTests : IDisposable
{
    private readonly string _folder;

    public HandReviewTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "humblerenamer-review-" + Guid.NewGuid().ToString("N"));
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
        // Metadata reading is off, so candidates come from the filename alone.
        var options = new RenameOptions { UseEmbeddedMetadata = false };
        var planner = new RenamePlanner(TestEngine.Current, options);
        return await planner.BuildAsync(_folder);
    }

    [Fact]
    public async Task LeadingCandidateMatchesTheProposedName()
    {
        CreateFiles("warmother.cbz");

        var action = (await PlanAsync()).Actions.Single();

        Assert.NotEmpty(action.Candidates);
        Assert.Equal(action.ProposedName, action.Candidates[0].Name);
        Assert.Equal("War Mother.cbz", action.Candidates[0].Name);
    }

    [Fact]
    public async Task KeepingTheCurrentNameIsAlwaysOffered()
    {
        CreateFiles("warmother.cbz");

        var action = (await PlanAsync()).Actions.Single();

        var keep = Assert.Single(action.Candidates, c => c.Name == "warmother.cbz");
        Assert.Equal("keep current name", keep.Label);
    }

    [Fact]
    public async Task IdenticalReadingsAreCollapsed()
    {
        // Filename and merged reading agree, so only one titled candidate plus "keep".
        CreateFiles("warmother.cbz");

        var action = (await PlanAsync()).Actions.Single();

        Assert.Equal(2, action.Candidates.Count);
        Assert.Equal("from filename", action.Candidates[0].Label);
    }

    [Fact]
    public async Task ChoosingTheKeepCandidateLeavesTheFileUnchanged()
    {
        CreateFiles("warmother.cbz");
        var plan = await PlanAsync();

        var revised = RenamePlanner.RebuildWithChosenNames(
            plan, new Dictionary<int, string> { [0] = "warmother" });

        var action = revised.Actions.Single();
        Assert.Equal(RenameStatus.Unchanged, action.Status);
        Assert.Equal("warmother.cbz", action.ProposedName);
    }

    [Fact]
    public async Task ATypedNameOverridesTheGuessAndKeepsTheExtension()
    {
        CreateFiles("warmother.cbz");
        var plan = await PlanAsync();

        var revised = RenamePlanner.RebuildWithChosenNames(
            plan, new Dictionary<int, string> { [0] = "War Mother - Special" });

        var action = revised.Actions.Single();
        Assert.Equal(RenameStatus.Rename, action.Status);
        Assert.Equal("War Mother - Special.cbz", action.ProposedName);
    }

    [Fact]
    public async Task AnUnpickedFileKeepsTheScanProposal()
    {
        CreateFiles("warmother.cbz", "whitesand.cbz");
        var plan = await PlanAsync();

        // Only the first file is hand-picked; the second falls back to its proposal.
        var revised = RenamePlanner.RebuildWithChosenNames(
            plan, new Dictionary<int, string> { [0] = "warmother" });

        Assert.Equal("warmother.cbz", revised.Actions[0].ProposedName);
        Assert.Equal("White Sand.cbz", revised.Actions[1].ProposedName);
    }

    [Fact]
    public async Task TwoTypedNamesThatCollideGetDistinctTargets()
    {
        CreateFiles("warmother.cbz", "whitesand.cbz");
        var plan = await PlanAsync();

        var revised = RenamePlanner.RebuildWithChosenNames(
            plan, new Dictionary<int, string> { [0] = "Anthology", [1] = "Anthology" });

        Assert.Equal("Anthology.cbz", revised.Actions[0].ProposedName);
        Assert.Equal("Anthology (2).cbz", revised.Actions[1].ProposedName);
    }
}
