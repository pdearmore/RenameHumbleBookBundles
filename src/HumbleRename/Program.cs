using System.Reflection;
using HumbleRename.Cli;
using HumbleRename.Lookup;
using HumbleRename.Naming;
using HumbleRename.Renaming;

// hbrename — rename Humble Bundle comics and ebooks to proper titles.
//
// Nothing is written to disk until the full before/after list has been shown and
// confirmed, and every applied run leaves an undo log behind.

const int ExitOk = 0;
const int ExitError = 1;
const int ExitUsage = 2;

ConsoleUi.EnableUnicodeOutput();

if (!CommandLineOptions.TryParse(args, out var options, out var parseError))
{
    ConsoleUi.Error(parseError!);
    return ExitUsage;
}

if (options.Help)
{
    Console.WriteLine(CommandLineOptions.HelpText);
    return ExitOk;
}

if (options.Version)
{
    var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    Console.WriteLine($"hbrename {version}");
    return ExitOk;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    // Let the in-flight operation unwind rather than killing the process mid-rename.
    e.Cancel = true;
    cancellation.Cancel();
    ConsoleUi.Warn("\n  Cancelling...");
};

try
{
    return await RunAsync(options, cancellation.Token);
}
catch (OperationCanceledException)
{
    ConsoleUi.Warn("Cancelled. Nothing further was changed.");
    return ExitError;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
{
    ConsoleUi.Error(ex.Message);
    return ExitError;
}

async Task<int> RunAsync(CommandLineOptions cli, CancellationToken cancellationToken)
{
    var folder = ResolveFolder(cli.Folder);
    if (folder is null)
    {
        ConsoleUi.Error("No folder given.");
        return ExitUsage;
    }

    if (!Directory.Exists(folder))
    {
        ConsoleUi.Error($"Folder not found: {folder}");
        return ExitError;
    }

    if (cli.Undo)
    {
        return Revert(folder);
    }

    var engine = NamingEngine.Create(cli.LexiconPath);

    LookupService? lookup = null;
    if (cli.Online)
    {
        lookup = LookupService.Create(
            cli.ComicVineKey,
            cli.GoogleBooksKey,
            cli.Confidence);

        var providers = lookup.ActiveProviders.ToList();
        ConsoleUi.Muted($"  Online lookup enabled: {string.Join(", ", providers)}");

        if (!providers.Contains("comicvine"))
        {
            ConsoleUi.Muted("  (set HBRENAME_COMICVINE_KEY for far better comic matching)");
        }
    }

    try
    {
        var planner = new RenamePlanner(engine, cli.ToRenameOptions(), lookup);

        var progress = new Progress<(int Done, int Total, string Current)>(
            report => ConsoleUi.Progress(report.Done, report.Total, report.Current));

        var plan = await planner.BuildAsync(folder, progress, cancellationToken);
        ConsoleUi.ClearProgress();

        if (plan.Actions.Count == 0)
        {
            ConsoleUi.Warn("No matching files found. Try --ext or --all-files.");
            return ExitOk;
        }

        ConsoleUi.WritePlan(plan);

        if (cli.DryRun)
        {
            ConsoleUi.Muted("\n  Dry run - nothing was changed.");
            return ExitOk;
        }

        if (plan.ChangeCount == 0)
        {
            ConsoleUi.Muted("\n  Every file is already named correctly.");
            return ExitOk;
        }

        if (!cli.AssumeYes && !ConsoleUi.Confirm($"Rename {plan.ChangeCount} file(s)?"))
        {
            ConsoleUi.Muted("  Nothing was changed.");
            return ExitOk;
        }

        var outcome = RenameExecutor.Apply(plan);

        Console.WriteLine();
        ConsoleUi.WriteLine($"  Renamed {outcome.Succeeded} file(s).", ConsoleColor.Green);

        foreach (var failure in outcome.Failures)
        {
            ConsoleUi.Error($"  {failure}");
        }

        if (outcome.Succeeded == 0)
        {
            return ExitError;
        }

        // The point of the preview is that mistakes are cheap — offer the way back
        // immediately, while the list is still on screen.
        if (!cli.AssumeYes && ConsoleUi.Confirm("Revert these renames?"))
        {
            var log = outcome.Undo;
            if (log is null)
            {
                ConsoleUi.Error("  No undo information was recorded.");
                return ExitError;
            }

            var reverted = RenameExecutor.Revert(log);
            ConsoleUi.WriteLine($"  Put {reverted.Succeeded} file(s) back.", ConsoleColor.Green);

            foreach (var failure in reverted.Failures)
            {
                ConsoleUi.Error($"  {failure}");
            }

            UndoLog.Delete(plan.Root);
            return ExitOk;
        }

        ConsoleUi.Muted($"  Changed your mind later? Run:  hbrename \"{folder}\" --undo");
        return ExitOk;
    }
    finally
    {
        lookup?.Dispose();
    }
}

int Revert(string folder)
{
    var log = UndoLog.Load(folder);
    if (log is null)
    {
        ConsoleUi.Error($"No undo log in {folder}. There is nothing to put back.");
        return ExitError;
    }

    ConsoleUi.Heading($"Undo - {log.Entries.Count} rename(s) from {log.TimestampUtc.ToLocalTime():g}");

    foreach (var entry in log.Entries)
    {
        Console.Write("  ");
        ConsoleUi.Write(entry.To, ConsoleColor.DarkYellow);
        ConsoleUi.Write("  ->  ", ConsoleColor.DarkGray);
        ConsoleUi.WriteLine(entry.From, ConsoleColor.Green);
    }

    if (!ConsoleUi.Confirm($"Put {log.Entries.Count} file(s) back?", defaultAnswer: true))
    {
        ConsoleUi.Muted("  Nothing was changed.");
        return ExitOk;
    }

    var outcome = RenameExecutor.Revert(log);
    ConsoleUi.WriteLine($"  Put {outcome.Succeeded} file(s) back.", ConsoleColor.Green);

    foreach (var failure in outcome.Failures)
    {
        ConsoleUi.Error($"  {failure}");
    }

    UndoLog.Delete(folder);
    return outcome.Failures.Count > 0 ? ExitError : ExitOk;
}

static string? ResolveFolder(string? supplied)
{
    if (!string.IsNullOrWhiteSpace(supplied))
    {
        return Path.GetFullPath(supplied.Trim('"'));
    }

    // Matching the original tool: ask when nothing was passed.
    var entered = ConsoleUi.PromptForFolder();
    return string.IsNullOrWhiteSpace(entered) ? null : Path.GetFullPath(entered);
}
