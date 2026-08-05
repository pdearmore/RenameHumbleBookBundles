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

    /// <summary>Comic Vine key from a paste or the saved store; takes precedence over the environment.</summary>
    private string? _comicVineKey;

    /// <summary>True when <see cref="_comicVineKey"/> is persisted to the profile, not just this run.</summary>
    private bool _comicVineKeySaved;

    /// <summary>Where a free Comic Vine API key is created.</summary>
    private const string ComicVineKeyUrl = "https://comicvine.gamespot.com/api/";

    public InteractiveSession(string? initialFolder, string version)
    {
        _version = version;

        // Pick up a key and the menu choices remembered from a previous run.
        _comicVineKey = ComicVineKeyStore.Load();
        _comicVineKeySaved = _comicVineKey is not null;
        LoadSettings();

        // A folder passed on the command line or dragged onto the exe wins over the saved one.
        if (!string.IsNullOrWhiteSpace(initialFolder))
        {
            var resolved = TryResolveFolder(initialFolder);
            if (resolved is not null)
            {
                _folder = resolved;
            }
        }
    }

    /// <summary>Applies the settings remembered from a previous run.</summary>
    private void LoadSettings()
    {
        var saved = SessionSettings.Load();

        // A folder that has since moved or been deleted is quietly forgotten.
        if (!string.IsNullOrWhiteSpace(saved.Folder) && Directory.Exists(saved.Folder))
        {
            _folder = saved.Folder;
        }

        if (!string.IsNullOrWhiteSpace(saved.CustomTemplate))
        {
            _customTemplate = saved.CustomTemplate;
        }
        else if (saved.TemplateIndex >= 0 && saved.TemplateIndex < TemplatePresets.Length)
        {
            _templateIndex = saved.TemplateIndex;
        }

        if (saved.CustomExtensions is { Count: > 0 })
        {
            _customExtensions = new HashSet<string>(saved.CustomExtensions, StringComparer.OrdinalIgnoreCase);
        }
        else if (saved.FileTypeIndex >= 0 && saved.FileTypeIndex < FileTypePresets.Length)
        {
            _fileTypeIndex = saved.FileTypeIndex;
        }

        _recurse = saved.Recurse;
        _online = saved.Online;
        _readMetadata = saved.ReadMetadata;
        _hydrateCloudFiles = saved.HydrateCloudFiles;
    }

    /// <summary>Remembers the current menu choices for next time (the key persists separately).</summary>
    private void SaveSettings() =>
        new SessionSettings
        {
            Folder = _folder,
            TemplateIndex = _templateIndex,
            CustomTemplate = _customTemplate,
            FileTypeIndex = _fileTypeIndex,
            CustomExtensions = _customExtensions?.ToArray(),
            Recurse = _recurse,
            Online = _online,
            ReadMetadata = _readMetadata,
            HydrateCloudFiles = _hydrateCloudFiles,
        }.Save();

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

            var choice = ConsoleUi.ReadChoice();

            switch (choice)
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
                case '8':
                    ChooseComicVineKey();
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

            // A setting or the folder may have just changed; remember it for next time.
            if (choice is >= '1' and <= '7' or 'S')
            {
                SaveSettings();
            }
        }
    }

    private void DrawMainMenu()
    {
        ConsoleUi.Section("Main Menu");

        ConsoleUi.MenuItem("1", "Folder", _folder ?? "(not set)");
        ConsoleUi.MenuItem("2", "Name format", TemplateName);
        ConsoleUi.MenuItem("3", "Include subfolders", OnOff(_recurse));
        ConsoleUi.MenuItem("4", "Online lookup", OnlineLookupStatus());
        ConsoleUi.MenuItem("5", "Read file metadata", OnOff(_readMetadata));
        ConsoleUi.MenuItem("6", "File types", FileTypeName);
        ConsoleUi.MenuItem("7", "Download cloud files", OnOff(_hydrateCloudFiles));
        ConsoleUi.MenuItem("8", "Comic Vine key", ComicVineKeyStatus);

        Console.WriteLine();
        ConsoleUi.MenuItem("S", "Scan and preview");
        ConsoleUi.MenuItem("U", "Undo the last run in this folder");
        ConsoleUi.MenuItem("Q", "Quit");

        if (!HasComicVineKey)
        {
            Console.WriteLine();
            ConsoleUi.Warn("   Comic Vine key not set - comics match far better with one, and it's free.");
            ConsoleUi.Muted($"   Add one under [8], or get it at {ComicVineKeyUrl}");
        }

        ConsoleUi.Prompt("Choose");
    }

    private static string OnOff(bool value) => value ? "Yes" : "No";

    /// <summary>Flags an enabled lookup that has no Comic Vine key — its weakest spot for comics.</summary>
    private string OnlineLookupStatus() =>
        _online && !HasComicVineKey ? "Yes (no Comic Vine key)" : OnOff(_online);

    /// <summary>True when a Comic Vine key is available, from this session or the environment.</summary>
    private bool HasComicVineKey =>
        !string.IsNullOrWhiteSpace(_comicVineKey) ||
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HUMBLERENAMER_COMICVINE_KEY"));

    private string ComicVineKeyStatus =>
        !string.IsNullOrWhiteSpace(_comicVineKey) ? (_comicVineKeySaved ? "Saved" : "Set for this session")
        : !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HUMBLERENAMER_COMICVINE_KEY")) ? "From environment"
        : "Not set";

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

    private void ChooseComicVineKey()
    {
        ConsoleUi.Section("Comic Vine Key");

        ConsoleUi.Muted("  Comic Vine is by far the best source for comics, and a key is free.");
        ConsoleUi.Muted("  It only comes into play when online lookup [4] is on.");
        Console.WriteLine();
        ConsoleUi.Write("  Current: ", ConsoleColor.Gray);
        ConsoleUi.WriteLine(ComicVineKeyStatus, ConsoleColor.Cyan);
        Console.WriteLine();

        ConsoleUi.MenuItem("P", "Paste a key for this session");
        ConsoleUi.MenuItem("G", "Get a free key (opens the signup page)");
        ConsoleUi.MenuItem("C", "Clear the session key");
        ConsoleUi.MenuItem("B", "Back");
        ConsoleUi.Prompt("Choose");

        switch (ConsoleUi.ReadChoice())
        {
            case 'P':
                PasteComicVineKey();
                break;
            case 'G':
                OpenInDefaultViewer(ComicVineKeyUrl);
                break;
            case 'C':
                _comicVineKey = null;
                _comicVineKeySaved = false;
                ComicVineKeyStore.Delete();
                Console.WriteLine();
                ConsoleUi.Muted("  Saved key removed.");
                ConsoleUi.Pause();
                break;
        }
    }

    private void PasteComicVineKey()
    {
        Console.WriteLine();
        ConsoleUi.Muted($"  Paste the key shown at {ComicVineKeyUrl} when signed in.");
        ConsoleUi.Muted("  It is saved to your user profile, encrypted, not the app folder. Blank to cancel.");
        Console.WriteLine();
        ConsoleUi.Write("  Key: ", ConsoleColor.White);

        var input = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        _comicVineKey = input.Trim();
        _comicVineKeySaved = ComicVineKeyStore.Save(_comicVineKey);

        Console.WriteLine();
        if (_comicVineKeySaved)
        {
            ConsoleUi.WriteLine("  Key saved - it will be remembered next time.", ConsoleColor.Green);
        }
        else
        {
            ConsoleUi.Warn("  Key set for this session (it could not be saved for next time).");
        }

        ConsoleUi.Pause();
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
                lookup = LookupService.Create(_comicVineKey);
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
    /// Walks the files with the arrow keys, showing the names the tool derived so the
    /// user can pick one, type their own, look it up online, or keep the current name.
    /// Up/Down move the highlight through the current file's options; Left/Right (and
    /// Enter for next) move between files, keeping whatever option is highlighted. Each
    /// file's candidates and choice persist across navigation, so moving back and forth
    /// never loses anything.
    /// </summary>
    private async Task<RenamePlan> HandReviewAsync(
        RenamePlan plan,
        RenamePlanner planner,
        LookupService? scanLookup,
        CancellationToken cancellationToken)
    {
        var count = plan.Actions.Count;
        if (count == 0)
        {
            return plan;
        }

        // Per-file state kept for the whole review so moving back and forth never loses
        // a fetched candidate or a chosen answer.
        var lists = new List<NameCandidate>[count];
        var selected = new int[count];
        var visited = new bool[count];

        // Reuse the scan's catalogue connection when online was on; otherwise open one
        // lazily the first time the user asks, and dispose only what we opened here.
        var lookup = scanLookup;
        var createdLookup = false;

        try
        {
            var i = 0;
            var finished = false;

            while (!finished && i < count)
            {
                var action = plan.Actions[i];

                if (!visited[i])
                {
                    lists[i] = [.. DisplayCandidates(action)];
                    selected[i] = IndexOfName(lists[i], action.ProposedName);
                    visited[i] = true;
                }

                var candidates = lists[i];

                ConsoleUi.TryClear();
                SplashScreen.ShowCompact(_version);
                DrawReviewFile(i + 1, count, action, candidates, selected[i]);

                var pressed = ConsoleUi.ReadKeyInfo();
                var ch = char.ToUpperInvariant(pressed.KeyChar);

                // Up/Down move the highlight within this file; Left/Right move between
                // files. The '-' '+' ',' '.' stand-ins keep the flow scriptable in tests.
                var selectUp = pressed.Key is ConsoleKey.UpArrow || ch == '-';
                var selectDown = pressed.Key is ConsoleKey.DownArrow || ch == '+';
                var toPrevious = pressed.Key is ConsoleKey.LeftArrow || ch is ',' or '<';
                var toNext = pressed.Key is ConsoleKey.RightArrow or ConsoleKey.Enter || ch is '.' or '>';

                if (selectUp)
                {
                    if (selected[i] > 0)
                    {
                        selected[i]--;
                    }
                }
                else if (selectDown)
                {
                    if (selected[i] < candidates.Count - 1)
                    {
                        selected[i]++;
                    }
                }
                else if (toPrevious)
                {
                    if (i > 0)
                    {
                        i--;
                    }
                }
                else if (toNext)
                {
                    // Move to the next file keeping the highlighted option (in selected[i]).
                    i++;
                }
                else if (char.IsDigit(ch))
                {
                    var pick = ch - '1';
                    if (pick >= 0 && pick < candidates.Count)
                    {
                        selected[i] = pick;
                        i++;
                    }
                }
                else if (ch == 'E')
                {
                    var custom = PromptForCustomName(action.OriginalName);
                    if (custom is not null)
                    {
                        candidates.Add(new NameCandidate
                        {
                            Label = "your name",
                            Name = custom + Path.GetExtension(action.OriginalName),
                        });
                        selected[i] = candidates.Count - 1;
                        i++;
                    }
                }
                else if (ch == 'S')
                {
                    selected[i] = IndexOfName(candidates, action.OriginalName);
                    i++;
                }
                else if (ch == 'O')
                {
                    // Look at the actual file; stay on it afterwards.
                    OpenInDefaultViewer(action.OriginalPath);
                }
                else if (ch == 'L')
                {
                    if (lookup is null)
                    {
                        lookup = LookupService.Create(_comicVineKey);
                        createdLookup = true;
                    }

                    var added = await AddOnlineCandidateAsync(
                        planner, lookup, action, candidates, cancellationToken);
                    if (added >= 0)
                    {
                        selected[i] = added; // highlight the freshly fetched name
                    }
                }
                else if (ch == 'Q')
                {
                    finished = true;
                }
            }

            var chosen = new Dictionary<int, string>();
            for (var r = 0; r < count; r++)
            {
                if (visited[r])
                {
                    chosen[r] = BaseNameOf(lists[r][selected[r]].Name);
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

    /// <summary>Index of the candidate whose name matches, or 0 (the top choice) if none do.</summary>
    private static int IndexOfName(List<NameCandidate> candidates, string name)
    {
        for (var c = 0; c < candidates.Count; c++)
        {
            if (string.Equals(candidates[c].Name, name, StringComparison.Ordinal))
            {
                return c;
            }
        }

        return 0;
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
        ConsoleUi.MenuItem("Q", "Finish review now");
        ConsoleUi.Muted("  Up/Down choose an option; Left/Right move between files (Enter = next).");
        ConsoleUi.Muted("  A number also picks an option and moves on.");
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
