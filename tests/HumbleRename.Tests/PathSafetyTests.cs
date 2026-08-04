using HumbleRename.Renaming;

namespace HumbleRename.Tests;

public class PathSafetyTests
{
    [Theory]
    // A colon almost always separates a title from its subtitle.
    [InlineData("Divinity: The Complete Trilogy", "Divinity - The Complete Trilogy")]
    // Question marks are legal in titles but not in filenames.
    [InlineData("Can You Just Die, My Darling?", "Can You Just Die, My Darling")]
    [InlineData("Wolverine/Punisher", "Wolverine-Punisher")]
    [InlineData("He Said \"Run\"", "He Said 'Run'")]
    [InlineData("Star*Man", "Star+Man")]
    [InlineData("A|B", "A-B")]
    public void ReplacesCharactersWindowsForbids(string input, string expected) =>
        Assert.Equal(expected, PathSafety.MakeSafeFileName(input));

    [Fact]
    public void StripsTrailingDotsAndSpaces() =>
        // Windows silently drops these, which would desynchronise our record of the name.
        Assert.Equal("Volume One", PathSafety.MakeSafeFileName("Volume One. "));

    [Fact]
    public void EscapesReservedDeviceNames() =>
        Assert.Equal("_CON", PathSafety.MakeSafeFileName("CON"));

    [Fact]
    public void CollapsesRepeatedWhitespace() =>
        Assert.Equal("Red Team", PathSafety.MakeSafeFileName("Red    Team"));

    [Fact]
    public void TruncatesOverlyLongNames()
    {
        var result = PathSafety.MakeSafeFileName(new string('a', 400));

        Assert.Equal(PathSafety.MaxNameLength, result.Length);
    }

    [Fact]
    public void EmptyInputBecomesUntitled() =>
        Assert.Equal("Untitled", PathSafety.MakeSafeFileName("   "));

    [Fact]
    public void SuffixesNamesAlreadyClaimedInThisRun()
    {
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directory = Path.Combine(Path.GetTempPath(), "hbrename-tests-" + Guid.NewGuid().ToString("N"));

        var first = PathSafety.ResolveCollision(directory, "Saga", ".cbz", claimed);
        var second = PathSafety.ResolveCollision(directory, "Saga", ".cbz", claimed);
        var third = PathSafety.ResolveCollision(directory, "Saga", ".cbz", claimed);

        Assert.Equal("Saga.cbz", first);
        Assert.Equal("Saga (2).cbz", second);
        Assert.Equal("Saga (3).cbz", third);
    }
}
