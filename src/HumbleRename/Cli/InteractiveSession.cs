using System.ComponentModel;
using System.Diagnostics;
using HumbleRename.Lookup;
using HumbleRename.Naming;
using HumbleRename.Renaming;

namespace HumbleRename.Cli;

/// <summary>
/// The menu-driven front end: every setting is chosen on screen after launching,
/// so nothing has to be remembered as a command-line switch.
/// </summary>
public sealed class InteractiveSession
{
    /// <summary>Name layouts offered on the format menu, with a worked example of each.</summary>
    private static readonly (string Name, string Template, string Example)[] TemplatePresets =
    [
        ("Full descriptive title", NameTemplate.Default,
            "The Walking Dead Vol. 01 - Days Gone Bye (2003).cbz"),
        ("Scraper friendly (Komga, Kavita)", NameTemplate.Compact,
            "The Walking Dead v01 (2003).cbz"),
        ("Books - title and author", "{Title}[ - {Author}][ ({Year})]",
            "Dune - Frank Herbert (1965).epub"),
        ("Just fix casing and spacing", "{Title}",
            "The Walking Dead.cbz"),
    ];

    private static readonly (string Name, string[] Extensions)[] FileTypePresets =
    [
        ("Comics and ebooks",
            [".cbz", ".cbr", ".cb7", ".cbt", ".pdf", ".epub", ".mobi", ".azw3", ".zip", ".rar"]),
        ("Comics only", [".cbz", ".cbr", ".cb7", ".cbt"]),
        ("Ebooks only", [".pdf", ".epub", ".mobi", ".azw3"]),
        ("Every file, whatever the extension", []),
    ];

    private readonly string _version;

    private string? _folder;
    private int _templateIndex;
    private string? _customTemplate;
    private int _fileTypeIndex;
    private HashSet<string>? _customExtensions;
    private bool _recurse;
    private bool _online;
    private bool _readMetadata = true;
    private bool _hydrateCloudFiles;

    public InteractiveSession(string? initialFolder, string version)
    {
        _version = version;

        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            var resolved = TryResolveFolder(initialFolder);
            if (resolved is not null)
            {
                _folder = resolved;
            }
        }
    }

    private string ActiveTemplate => _customTemplate ?? TemplatePresets[_templateIndex].Template;

    private string TemplateName => _customTemplate is null
        ? TemplatePresets[_templateIndex].Name
        : "Custom";

    private string FileTypeName => _customExtensions is null
        ? FileTypePresets[_fileTypeIndex].Name
        : "Custom list";

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        SplashScreen.Show(_version);
        ConsoleUi.Muted("  Choose your options, then press S to scan. Nothing is renamed");
        ConsoleUi.Muted("  until you have seen the full before/after list and confirmed.");

        var firstPass = true;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!firstPass)
            {
                ConsoleUi.TryClear();
                SplashScreen.ShowCompact(_version);
            }

            firstPass = false;
            DrawMainMenu();

            switch (ConsoleUi.ReadChoice())
            {
                case '1':
                    ChooseFolder();
                    break;
                case '2':
                    ChooseTemplate();
                    break;
                case '3':
                    _recurse = !_recurse;
                    break;
                case '4':
                    _online = !_online;
                    break;
                case '5':
                    _readMetadata = !_readMetadata;
                    break;
                case '6':
                    ChooseFileTypes();
                    break;
                case '7':
                    _hydrateCloudFiles = !_hydrateCloudFiles;
                    break;
                case 'S':
                    await ScanAsync(cancellationToken);
                    break;
                case 'U':
                    UndoLastRun();
                    break;
                case 'Q':
                    Console.WriteLine();
                    ConsoleUi.Muted("  Bye.");
                    return 0;
            }
        }
    }

    private void DrawMainMenu()
    {
        ConsoleUi.Section("Main Menu");

        ConsoleUi.MenuItem("1", "Folder", _folder ?? "(not set)");
        ConsoleUi.MenuItem("2", "Name format", TemplateName);
        ConsoleUi.MenuItem("3", "Include subfolders", OnOff(_recurse));
        ConsoleUi.MenuItem("4", "Online lookup", OnOff(_online));
        ConsoleUi.MenuItem("5", "Read file metadata", OnOff(_readMetadata));
        ConsoleUi.MenuItem("6", "File types", FileTypeName);
        ConsoleUi.MenuItem("7", "Download cloud files", OnOff(_hydrateCloudFiles));

        Console.WriteLine();
        ConsoleUi.MenuItem("S", "Scan and preview");
        ConsoleUi.MenuItem("U", "Undo the last run in this folder");
        ConsoleUi.MenuItem("Q", "Quit");

        ConsoleUi.Prompt("Choose");
    }

    private static string OnOff(bool value) => value ? "Yes" : "No";

    private void ChooseFolder()
    {
        ConsoleUi.Section("Which Folder");

        if (_folder is not null)
        {
            ConsoleUi.Muted($"  Currently: {_folder}");
            Console.WriteLine();
        }

        ConsoleUi.Muted("  Type or paste a path, or drag the folder onto this window.");
        ConsoleUi.Muted("  Leave it blank to go back.");
        Console.WriteLine();
        ConsoleUi.Write("  Folder: ", ConsoleColor.White);

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var resolved = TryResolveFolder(input);
        if (resolved is null)
        {
            Console.WriteLine();
            ConsoleUi.Error($"  Not a folder: {input.Trim().Trim('"')}");
            ConsoleUi.Pause();
            return;
        }

        _folder = resolved;
    }

    private static string? TryResolveFolder(string input)
    {
        try
        {
            // Tolerate a path pasted or dragged in with quotes around it.
            var cleaned = input.Trim().Trim('"').Trim();
            if (cleaned.Length == 0)
            {
                return null;
            }

            var full = Path.GetFullPath(cleaned);
            return Directory.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private void ChooseTemplate()
    {
        ConsoleUi.Section("Name Format");

        for (var i = 0; i < TemplatePresets.Length; i++)
        {
            var (name, _, example) = TemplatePresets[i];
            var selected = _customTemplate is null && i == _templateIndex;

            ConsoleUi.Write(selected ? "   > [" : "     [", ConsoleColor.DarkGray);
            ConsoleUi.Write((i + 1).ToString(), ConsoleColor.Yellow);
            ConsoleUi.Write("]  ", ConsoleColor.DarkGray);
            ConsoleUi.WriteLine(name, selected ? ConsoleColor.White : ConsoleColor.Gray);
            ConsoleUi.Muted($"          {example}");
        }

        Console.WriteLine();
        ConsoleUi.MenuItem("C", "Custom template...");
        ConsoleUi.MenuItem("B", "Back");
        ConsoleUi.Prompt("Choose");

        var choice = ConsoleUi.ReadChoice();

        if (choice == 'C')
        {
            EnterCustomTemplate();
            return;
        }

        if (char.IsDigit(choice))
        {
            var index = choice - '1';
            if (index >= 0 && index < TemplatePresets.Length)
            {
                _templateIndex = index;
                _customTemplate = null;
            }
        }
    }

    private void EnterCustomTemplate()
    {
        ConsoleUi.Section("Custom Template");

        ConsoleUi.Muted("  Tokens:  Series  Title  Subtitle  Volume  Issue  Book  Year");
        ConsoleUi.Muted("           Author  Publisher  Editions");
        ConsoleUi.Muted("  A number format may follow a colon:  {Volume:00}");
        ConsoleUi.Muted("  A [bracketed] part vanishes when a token inside it is empty.");
        Console.WriteLine();
        ConsoleUi.Muted($"  Current:  {ActiveTemplate}");
        Console.WriteLine();
        ConsoleUi.Write("  Template (blank to cancel): ", ConsoleColor.White);

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        _customTemplate = input.Trim();

        // Show what it actually produces before committing to it.
        var sample = new Model.BookMetadata
        {
            Series = "The Walking Dead",
            Title = "The Walking Dead: Days Gone Bye",
            Subtitle = "Days Gone Bye",
            Volume = 1,
            Year = 2003,
            Author = "Robert Kirkman",
        };

        Console.WriteLine();
        ConsoleUi.Write("  Example: ", ConsoleColor.Gray);
        ConsoleUi.WriteLine(
            PathSafety.MakeSafeFileName(NameTemplate.Render(_customTemplate, sample)) + ".cbz",
            ConsoleColor.Green);

        ConsoleUi.Pause();
    }

    private void ChooseFileTypes()
    {
        ConsoleUi.Section("File Types");

        for (var i = 0; i < FileTypePresets.Length; i++)
        {
            var (name, extensions) = FileTypePresets[i];
            var selected = _customExtensions is null && i == _fileTypeIndex;

            ConsoleUi.Write(selected ? "   > [" : "     [", ConsoleColor.DarkGray);
            ConsoleUi.Write((i + 1).ToString(), ConsoleColor.Yellow);
            ConsoleUi.Write("]  ", ConsoleColor.DarkGray);
            ConsoleUi.WriteLine(name, selected ? ConsoleColor.White : ConsoleColor.Gray);

            if (extensions.Length > 0)
            {
                ConsoleUi.Muted("          " + string.Join("  ", extensions));
            }
        }

        Console.WriteLine();
        ConsoleUi.MenuItem("C", "Custom list...");
        ConsoleUi.MenuItem("B", "Back");
        ConsoleUi.Prompt("Choose");

        var choice = ConsoleUi.ReadChoice();

        if (choice == 'C')
        {
            EnterCustomExtensions();
            return;
        }

        if (char.IsDigit(choice))
        {
            var index = choice - '1';
            if (index >= 0 && index < FileTypePresets.Length)
            {
                _fileTypeIndex = index;
                _customExtensions = null;
            }
        }
    }

    private void EnterCustomExtensions()
    {
        ConsoleUi.Section("Custom File Types");
        ConsoleUi.Muted("  Comma separated, for example:  cbz, cbr, pdf");
        Console.WriteLine();
        ConsoleUi.Write("  Extensions (blank to cancel): ", ConsoleColor.White);

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in input.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(raw.StartsWith('.') ? raw : "." + raw);
        }

        _customExtensions = set.Count > 0 ? set : null;
    }

    private RenameOptions BuildOptions()
    {
        var extensions = _customExtensions ??
                         new HashSet<string>(FileTypePresets[_fileTypeIndex].Extensions, StringComparer.OrdinalIgnoreCase);

        return new RenameOptions
        {
            Template = ActiveTemplate,
            Recursive = _recurse,
            UseOnlineLookup = _online,
            UseEmbeddedMetadata = _readMetadata,
            HydrateCloudFiles = _hydrateCloudFiles,
            Extensions = extensions,
        };
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        if (_folder is null)
        {
            ConsoleUi.Section("No Folder Yet");
            ConsoleUi.Warn("  Pick a folder first with [1].");
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Section($"Scanning {_folder}");

        LookupService? lookup = null;
        try
        {
            if (_online)
            {
                lookup = LookupService.Create();
                ConsoleUi.Muted($"  Catalogues: {string.Join(", ", lookup.ActiveProviders)}");
            }

            var engine = NamingEngine.Create();
            var planner = new RenamePlanner(engine, BuildOptions(), lookup);

            var progress = new Progress<(int Done, int Total, string Current)>(
                report => ConsoleUi.Progress(report.Done, report.Total, report.Current));

            var plan = await planner.BuildAsync(_folder, progress, cancellationToken);
            ConsoleUi.ClearProgress();

            if (plan.Actions.Count == 0)
            {
                ConsoleUi.Warn("  No matching files here. Try a different folder or file type.");
                ConsoleUi.Pause();
                return;
            }

            ConsoleUi.WritePlan(plan);
            await ReviewPlanAsync(plan, planner, lookup, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            ConsoleUi.ClearProgress();
            ConsoleUi.Error($"  {ex.Message}");
            ConsoleUi.Pause();
        }
        finally
        {
            lookup?.Dispose();
        }
    }

    /// <summary>
    /// Offers the save / hand-review / back choice once the preview is on screen. Every
    /// scanned file is eligible for hand-review, so it is offered even when nothing would
    /// change by default.
    /// </summary>
    private async Task ReviewPlanAsync(
        RenamePlan plan,
        RenamePlanner planner,
        LookupService? scanLookup,
        CancellationToken cancellationToken)
    {
        if (plan.ChangeCount == 0)
        {
            ConsoleUi.Section("All Correct");
            ConsoleUi.Muted("  Every file already matches its preferred name.");
            ConsoleUi.MenuItem("H", "Hand-review each file anyway");
            ConsoleUi.MenuItem("B", "Back to the menu");
            ConsoleUi.Prompt("Choose");

            if (ConsoleUi.ReadChoice() == 'H')
            {
                await RunHandReviewAsync(plan, planner, scanLookup, cancellationToken);
            }
            else
            {
                Console.WriteLine();
                ConsoleUi.Muted("  Nothing was changed.");
                ConsoleUi.Pause();
            }

            return;
        }

        ConsoleUi.Section("Save?");
        ConsoleUi.MenuItem("A", $"Save these {plan.ChangeCount} rename(s)");
        ConsoleUi.MenuItem("H", "Hand-review each file, one at a time");
        ConsoleUi.MenuItem("B", "Back to the menu, change nothing");
        ConsoleUi.Prompt("Choose");

        switch (ConsoleUi.ReadChoice())
        {
            case 'A':
                ApplyPlan(plan);
                break;
            case 'H':
                await RunHandReviewAsync(plan, planner, scanLookup, cancellationToken);
                break;
            default:
                Console.WriteLine();
                ConsoleUi.Muted("  Nothing was changed.");
                ConsoleUi.Pause();
                break;
        }
    }

    /// <summary>Hand-reviews the plan, shows the revised list, then applies it.</summary>
    private async Task RunHandReviewAsync(
        RenamePlan plan,
        RenamePlanner planner,
        LookupService? scanLookup,
        CancellationToken cancellationToken)
    {
        var revised = await HandReviewAsync(plan, planner, scanLookup, cancellationToken);
        ConsoleUi.TryClear();
        ConsoleUi.WritePlan(revised);
        ApplyPlan(revised);
    }

    /// <summary>
    /// Walks every file, showing the different names the tool derived so the user can
    /// pick one, type their own, look it up online, or keep the current name, then
    /// returns a revised plan.
    /// </summary>
    private async Task<RenamePlan> HandReviewAsync(
        RenamePlan plan,
        RenamePlanner planner,
        LookupService? scanLookup,
        CancellationToken cancellationToken)
    {
        var chosen = new Dictionary<int, string>();

        // Reuse the scan's catalogue connection when online was on; otherwise open one
        // lazily the first time the user asks, and dispose only what we opened here.
        var lookup = scanLookup;
        var createdLookup = false;

        try
        {
            for (var i = 0; i < plan.Actions.Count; i++)
            {
                var action = plan.Actions[i];
                var candidates = new List<NameCandidate>(DisplayCandidates(action));

                // The highlighted default is whichever candidate the scan settled on.
                var defaultIndex = 0;
                for (var c = 0; c < candidates.Count; c++)
                {
                    if (string.Equals(candidates[c].Name, action.ProposedName, StringComparison.Ordinal))
                    {
                        defaultIndex = c;
                        break;
                    }
                }

                var decided = false;
                while (!decided)
                {
                    ConsoleUi.TryClear();
                    SplashScreen.ShowCompact(_version);
                    DrawReviewFile(i + 1, plan.Actions.Count, action, candidates, defaultIndex);

                    var key = ConsoleUi.ReadChoice();

                    if (key is '\r' or '\n' or '\0')
                    {
                        chosen[i] = BaseNameOf(candidates[defaultIndex].Name);
                        decided = true;
                    }
                    else if (key == 'E')
                    {
                        var custom = PromptForCustomName(action.OriginalName);
                        if (custom is not null)
                        {
                            chosen[i] = custom;
                            decided = true;
                        }
                    }
                    else if (key == 'S')
                    {
                        chosen[i] = Path.GetFileNameWithoutExtension(action.OriginalName);
                        decided = true;
                    }
                    else if (key == 'O')
                    {
                        // Look at the actual file before deciding; stay on it afterwards.
                        OpenInDefaultViewer(action.OriginalPath);
                    }
                    else if (key == 'L')
                    {
                        if (lookup is null)
                        {
                            lookup = LookupService.Create();
                            createdLookup = true;
                        }

                        var added = await AddOnlineCandidateAsync(
                            planner, lookup, action, candidates, cancellationToken);
                        if (added >= 0)
                        {
                            defaultIndex = added; // highlight the freshly fetched name
                        }
                    }
                    else if (key == 'Q')
                    {
                        // Finish now: files not yet reached keep their scan proposal.
                        return RenamePlanner.RebuildWithChosenNames(plan, chosen);
                    }
                    else if (char.IsDigit(key))
                    {
                        var pick = key - '1';
                        if (pick >= 0 && pick < candidates.Count)
                        {
                            chosen[i] = BaseNameOf(candidates[pick].Name);
                            decided = true;
                        }
                    }
                }
            }

            return RenamePlanner.RebuildWithChosenNames(plan, chosen);
        }
        finally
        {
            if (createdLookup)
            {
                lookup!.Dispose();
            }
        }
    }

    /// <summary>
    /// Looks the current file up online and appends a confident match to its candidate
    /// list. Returns the index to highlight, or -1 when nothing usable came back.
    /// </summary>
    private static async Task<int> AddOnlineCandidateAsync(
        RenamePlanner planner,
        LookupService lookup,
        RenameAction action,
        List<NameCandidate> candidates,
        CancellationToken cancellationToken)
    {
        Console.WriteLine();
        ConsoleUi.Muted("  Looking online...");

        // Providers swallow their own network and parse failures, so this returns null
        // rather than throwing when a catalogue is unreachable or has nothing confident.
        var candidate = await planner.LookUpOnlineAsync(action, lookup, cancellationToken);
        if (candidate is null)
        {
            ConsoleUi.Warn("  No confident match found online.");
            ConsoleUi.Pause();
            return -1;
        }

        // If that exact name is already listed, just point the highlight at it.
        for (var c = 0; c < candidates.Count; c++)
        {
            if (string.Equals(candidates[c].Name, candidate.Name, StringComparison.Ordinal))
            {
                return c;
            }
        }

        candidates.Add(candidate);
        return candidates.Count - 1;
    }

    private static IReadOnlyList<NameCandidate> DisplayCandidates(RenameAction action) =>
        action.Candidates.Count > 0
            ? action.Candidates
            : [new NameCandidate { Label = "keep current name", Name = action.OriginalName }];

    private static void DrawReviewFile(
        int position,
        int total,
        RenameAction action,
        IReadOnlyList<NameCandidate> candidates,
        int defaultIndex)
    {
        ConsoleUi.Section($"Hand-Review  {position} of {total}");

        Console.Write("   ");
        ConsoleUi.WriteLine(action.OriginalName, ConsoleColor.DarkYellow);
        Console.WriteLine();

        for (var c = 0; c < candidates.Count; c++)
        {
            var candidate = candidates[c];
            var isDefault = c == defaultIndex;

            ConsoleUi.Write(isDefault ? "   > [" : "     [", ConsoleColor.DarkGray);
            ConsoleUi.Write((c + 1).ToString(), ConsoleColor.Yellow);
            ConsoleUi.Write("]  ", ConsoleColor.DarkGray);
            ConsoleUi.Write(candidate.Name, isDefault ? ConsoleColor.Green : ConsoleColor.Gray);
            ConsoleUi.WriteLine($"   {candidate.Label}", ConsoleColor.DarkGray);
        }

        Console.WriteLine();
        ConsoleUi.MenuItem("O", "Open the file in its default app");
        ConsoleUi.MenuItem("L", "Look it up in the online catalogues");
        ConsoleUi.MenuItem("E", "Type my own name");
        ConsoleUi.MenuItem("S", "Skip - keep the current name");
        ConsoleUi.MenuItem("Q", "Finish review, keep the rest as previewed");
        ConsoleUi.Muted("  A number picks a name; Enter takes the highlighted one.");
        ConsoleUi.Prompt("Choose");
    }

    /// <summary>
    /// Opens a file with whatever application the OS has associated with its type, so a
    /// reviewer can see what it actually is before naming it. Failures are reported, not
    /// thrown — a missing association must not derail the review.
    /// </summary>
    private static void OpenInDefaultViewer(string path)
    {
        try
        {
            // UseShellExecute lets the shell pick the registered viewer for the type.
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException
                                       or FileNotFoundException or PlatformNotSupportedException)
        {
            Console.WriteLine();
            ConsoleUi.Error($"  Could not open the file: {ex.Message}");
            ConsoleUi.Pause();
        }
    }

    /// <summary>Prompts for a hand-typed name, returning the sanitised stem or null if cancelled.</summary>
    private static string? PromptForCustomName(string originalName)
    {
        var extension = Path.GetExtension(originalName);

        Console.WriteLine();
        if (extension.Length > 0)
        {
            ConsoleUi.Muted($"  The {extension} extension is added for you.");
        }

        ConsoleUi.Write("  New name (blank to cancel): ", ConsoleColor.White);
        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmed = input.Trim();

        // Forgive a typed-in extension so it is not doubled.
        if (extension.Length > 0 && trimmed.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[..^extension.Length];
        }

        var safe = PathSafety.MakeSafeFileName(trimmed);
        return safe.Length > 0 ? safe : null;
    }

    private static string BaseNameOf(string fileName) => Path.GetFileNameWithoutExtension(fileName);

    /// <summary>Applies a plan, then offers the keep/revert choice while the list is on screen.</summary>
    private void ApplyPlan(RenamePlan plan)
    {
        if (plan.ChangeCount == 0)
        {
            Console.WriteLine();
            ConsoleUi.Muted("  Nothing to change.");
            ConsoleUi.Pause();
            return;
        }

        var outcome = RenameExecutor.Apply(plan);

        Console.WriteLine();
        ConsoleUi.WriteLine($"  Renamed {outcome.Succeeded} file(s).", ConsoleColor.Green);

        foreach (var failure in outcome.Failures)
        {
            ConsoleUi.Error($"  {failure}");
        }

        if (outcome.Undo is null)
        {
            ConsoleUi.Pause();
            return;
        }

        // The revert offer belongs here, while the list is still on screen.
        ConsoleUi.Section("Happy With That?");
        ConsoleUi.MenuItem("K", "Keep the new names");
        ConsoleUi.MenuItem("R", "Revert - put every file back");
        ConsoleUi.Prompt("Choose");

        if (ConsoleUi.ReadChoice() == 'R')
        {
            var reverted = RenameExecutor.Revert(outcome.Undo);
            Console.WriteLine();
            ConsoleUi.WriteLine($"  Put {reverted.Succeeded} file(s) back.", ConsoleColor.Green);

            foreach (var failure in reverted.Failures)
            {
                ConsoleUi.Error($"  {failure}");
            }

            UndoLog.Delete(plan.Root);
        }
        else
        {
            Console.WriteLine();
            ConsoleUi.Muted("  Kept. You can still undo later from the main menu with [U].");
        }

        ConsoleUi.Pause();
    }

    private void UndoLastRun()
    {
        if (_folder is null)
        {
            ConsoleUi.Section("No Folder Yet");
            ConsoleUi.Warn("  Pick a folder first with [1].");
            ConsoleUi.Pause();
            return;
        }

        var log = UndoLog.Load(_folder);
        if (log is null)
        {
            ConsoleUi.Section("Nothing to Undo");
            ConsoleUi.Warn($"  No previous run recorded in {_folder}.");
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Section($"Undo {log.Entries.Count} rename(s) from {log.TimestampUtc.ToLocalTime():g}");

        foreach (var entry in log.Entries)
        {
            Console.Write("   ");
            ConsoleUi.Write(entry.To, ConsoleColor.DarkYellow);
            ConsoleUi.Write("  ->  ", ConsoleColor.DarkGray);
            ConsoleUi.WriteLine(entry.From, ConsoleColor.Green);
        }

        ConsoleUi.Section("Confirm");
        ConsoleUi.MenuItem("R", $"Revert all {log.Entries.Count}");
        ConsoleUi.MenuItem("B", "Back, change nothing");
        ConsoleUi.Prompt("Choose");

        if (ConsoleUi.ReadChoice() != 'R')
        {
            Console.WriteLine();
            ConsoleUi.Muted("  Nothing was changed.");
            ConsoleUi.Pause();
            return;
        }

        var outcome = RenameExecutor.Revert(log);
        Console.WriteLine();
        ConsoleUi.WriteLine($"  Put {outcome.Succeeded} file(s) back.", ConsoleColor.Green);

        foreach (var failure in outcome.Failures)
        {
            ConsoleUi.Error($"  {failure}");
        }

        UndoLog.Delete(_folder);
        ConsoleUi.Pause();
    }
}
