using HumbleRename.Lookup;
using HumbleRename.Naming;
using HumbleRename.Renaming;
using System.Diagnostics;

namespace HumbleRename.Cli;

/// <summary>
/// The menu-driven front end: every setting is chosen on screen after launching,
/// so nothing has to be remembered as a command-line switch.
/// </summary>
public sealed class InteractiveSession
{
    private const string FeedbackUrl = "https://github.com/pdearmore/RenameHumbleBookBundles/issues/new?template=feedback.yml";
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
                case 'F':
                    OpenFeedbackForm();
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
        ConsoleUi.Section("main menu");

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
        ConsoleUi.MenuItem("F", "Send feedback or report a problem");
        ConsoleUi.MenuItem("Q", "Quit");

        ConsoleUi.Prompt("choose");
    }

    private static string OnOff(bool value) => value ? "Yes" : "No";

    private static void OpenFeedbackForm()
    {
        try
        {
            Process.Start(new ProcessStartInfo(FeedbackUrl) { UseShellExecute = true });
            ConsoleUi.Muted("  Opening the feedback form in your browser...");
        }
        catch
        {
            ConsoleUi.Warn("  Could not open your browser. Visit:");
            ConsoleUi.Muted($"  {FeedbackUrl}");
        }

        ConsoleUi.Pause();
    }

    private void ChooseFolder()
    {
        ConsoleUi.Section("which folder");

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
        ConsoleUi.Section("name format");

        for (var i = 0; i < TemplatePresets.Length; i++)
        {
            var (name, _, example) = TemplatePresets[i];
            var selected = _customTemplate is null && i == _templateIndex;

            ConsoleUi.Write(selected ? "   > [" : "     [", ConsoleColor.DarkGray);
            ConsoleUi.Write((i + 1).ToString(), ConsoleColor.Green);
            ConsoleUi.Write("]  ", ConsoleColor.DarkGray);
            ConsoleUi.WriteLine(name, selected ? ConsoleColor.White : ConsoleColor.Gray);
            ConsoleUi.Muted($"          {example}");
        }

        Console.WriteLine();
        ConsoleUi.MenuItem("C", "Custom template...");
        ConsoleUi.MenuItem("B", "Back");
        ConsoleUi.Prompt("choose");

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
        ConsoleUi.Section("custom template");

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
        ConsoleUi.Section("file types");

        for (var i = 0; i < FileTypePresets.Length; i++)
        {
            var (name, extensions) = FileTypePresets[i];
            var selected = _customExtensions is null && i == _fileTypeIndex;

            ConsoleUi.Write(selected ? "   > [" : "     [", ConsoleColor.DarkGray);
            ConsoleUi.Write((i + 1).ToString(), ConsoleColor.Green);
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
        ConsoleUi.Prompt("choose");

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
        ConsoleUi.Section("custom file types");
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
            ConsoleUi.Section("no folder yet");
            ConsoleUi.Warn("  Pick a folder first with [1].");
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Section($"scanning {_folder}");

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
            ReviewPlan(plan);
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

    /// <summary>Offers the apply/back choice once the preview is on screen.</summary>
    private void ReviewPlan(RenamePlan plan)
    {
        if (plan.ChangeCount == 0)
        {
            Console.WriteLine();
            ConsoleUi.Muted("  Every file is already named correctly.");
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Section("apply?");
        ConsoleUi.MenuItem("A", $"Apply these {plan.ChangeCount} rename(s)");
        ConsoleUi.MenuItem("B", "Back to the menu, change nothing");
        ConsoleUi.Prompt("choose");

        if (ConsoleUi.ReadChoice() != 'A')
        {
            Console.WriteLine();
            ConsoleUi.Muted("  Nothing was changed.");
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
        ConsoleUi.Section("happy with that?");
        ConsoleUi.MenuItem("K", "Keep the new names");
        ConsoleUi.MenuItem("R", "Revert - put every file back");
        ConsoleUi.Prompt("choose");

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
            ConsoleUi.Section("no folder yet");
            ConsoleUi.Warn("  Pick a folder first with [1].");
            ConsoleUi.Pause();
            return;
        }

        var log = UndoLog.Load(_folder);
        if (log is null)
        {
            ConsoleUi.Section("nothing to undo");
            ConsoleUi.Warn($"  No previous run recorded in {_folder}.");
            ConsoleUi.Pause();
            return;
        }

        ConsoleUi.Section($"undo {log.Entries.Count} rename(s) from {log.TimestampUtc.ToLocalTime():g}");

        foreach (var entry in log.Entries)
        {
            Console.Write("   ");
            ConsoleUi.Write(entry.To, ConsoleColor.DarkGreen);
            ConsoleUi.Write("  ->  ", ConsoleColor.DarkGray);
            ConsoleUi.WriteLine(entry.From, ConsoleColor.Green);
        }

        ConsoleUi.Section("confirm");
        ConsoleUi.MenuItem("R", $"Revert all {log.Entries.Count}");
        ConsoleUi.MenuItem("B", "Back, change nothing");
        ConsoleUi.Prompt("choose");

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
