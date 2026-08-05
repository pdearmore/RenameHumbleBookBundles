using HumbleRename.Renaming;

namespace HumbleRename.Cli;

/// <summary>Parsed command line for one invocation.</summary>
public sealed class CommandLineOptions
{
    public string? Folder { get; private set; }

    public string Template { get; private set; } = NameTemplate.Default;

    public bool Recurse { get; private set; }

    public bool Online { get; private set; }

    public bool NoMetadata { get; private set; }

    public bool Hydrate { get; private set; }

    /// <summary>Apply without asking. Intended for scripts.</summary>
    public bool AssumeYes { get; private set; }

    /// <summary>Preview only; never touch the disk.</summary>
    public bool DryRun { get; private set; }

    /// <summary>Revert the previous run in this folder and exit.</summary>
    public bool Undo { get; private set; }

    public bool AllFiles { get; private set; }

    public bool Help { get; private set; }

    public bool Version { get; private set; }

    public string? ComicVineKey { get; private set; }

    public string? GoogleBooksKey { get; private set; }

    public string? LexiconPath { get; private set; }

    public double Confidence { get; private set; } = Lookup.LookupService.DefaultMinimumConfidence;

    public HashSet<string>? Extensions { get; private set; }

    public static bool TryParse(string[] args, out CommandLineOptions options, out string? error)
    {
        options = new CommandLineOptions();
        error = null;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    options.Help = true;
                    break;
                case "--version":
                    options.Version = true;
                    break;
                case "-r" or "--recurse" or "--recursive":
                    options.Recurse = true;
                    break;
                case "-o" or "--online":
                    options.Online = true;
                    break;
                case "--no-metadata":
                    options.NoMetadata = true;
                    break;
                case "--hydrate":
                    options.Hydrate = true;
                    break;
                case "-y" or "--yes":
                    options.AssumeYes = true;
                    break;
                case "-n" or "--dry-run":
                    options.DryRun = true;
                    break;
                case "-u" or "--undo":
                    options.Undo = true;
                    break;
                case "--all-files":
                    options.AllFiles = true;
                    break;

                case "-t" or "--template":
                    if (!TryTakeValue(args, ref i, out var template))
                    {
                        error = "--template needs a value.";
                        return false;
                    }

                    options.Template = template;
                    break;

                case "-e" or "--ext":
                    if (!TryTakeValue(args, ref i, out var extensions))
                    {
                        error = "--ext needs a comma-separated list, e.g. --ext cbz,cbr,pdf";
                        return false;
                    }

                    options.Extensions = ParseExtensions(extensions);
                    break;

                case "--comicvine-key":
                    if (!TryTakeValue(args, ref i, out var comicVine))
                    {
                        error = "--comicvine-key needs a value.";
                        return false;
                    }

                    options.ComicVineKey = comicVine;
                    break;

                case "--google-key":
                    if (!TryTakeValue(args, ref i, out var google))
                    {
                        error = "--google-key needs a value.";
                        return false;
                    }

                    options.GoogleBooksKey = google;
                    break;

                case "--lexicon":
                    if (!TryTakeValue(args, ref i, out var lexicon))
                    {
                        error = "--lexicon needs a path.";
                        return false;
                    }

                    options.LexiconPath = lexicon;
                    break;

                case "--confidence":
                    if (!TryTakeValue(args, ref i, out var confidence) ||
                        !double.TryParse(confidence, out var value) ||
                        value is < 0 or > 1)
                    {
                        error = "--confidence needs a number between 0 and 1.";
                        return false;
                    }

                    options.Confidence = value;
                    break;

                default:
                    if (arg.StartsWith('-'))
                    {
                        error = $"Unknown option '{arg}'. Run HumbleRenamer --help for the full list.";
                        return false;
                    }

                    if (options.Folder is not null)
                    {
                        error = $"More than one folder given ('{options.Folder}' and '{arg}').";
                        return false;
                    }

                    options.Folder = arg;
                    break;
            }
        }

        return true;
    }

    private static bool TryTakeValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return true;
    }

    private static HashSet<string> ParseExtensions(string list)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(raw.StartsWith('.') ? raw : "." + raw);
        }

        return set;
    }

    /// <summary>Builds the <see cref="RenameOptions"/> this invocation implies.</summary>
    public RenameOptions ToRenameOptions()
    {
        var options = new RenameOptions
        {
            Template = Template,
            Recursive = Recurse,
            UseOnlineLookup = Online,
            UseEmbeddedMetadata = !NoMetadata,
            HydrateCloudFiles = Hydrate,
            MinimumConfidence = Confidence,
        };

        if (AllFiles)
        {
            return options with { Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) };
        }

        return Extensions is not null ? options with { Extensions = Extensions } : options;
    }

    public const string HelpText = """
        HumbleRenamer — rename Humble Bundle comics and ebooks to proper titles.

        USAGE
          HumbleRenamer [folder] [options]

        If no folder is given, HumbleRenamer asks for one.
        Nothing is renamed until you see the full before/after list and confirm.

        OPTIONS
          -t, --template <fmt>   Name layout. Default:
                                 {Series}[ Vol. {Volume:00}][ Book {Book}][ #{Issue}]
                                 [ - {Subtitle}][ ({Year})][ ({Editions})]
                                 Tokens: Series Title Subtitle Volume Issue Book Year
                                         Author Publisher Editions
                                 A [bracketed] section vanishes when a token inside is empty.
          -r, --recurse          Include subfolders.
          -o, --online           Consult online catalogues for missing or clipped titles.
              --confidence <n>   Minimum match confidence, 0-1. Default 0.72.
              --comicvine-key <k>  Comic Vine API key (or set HUMBLERENAMER_COMICVINE_KEY).
              --google-key <k>     Google Books API key (or set HUMBLERENAMER_GOOGLE_BOOKS_KEY).
              --no-metadata      Do not read metadata from inside files.
              --hydrate          Download cloud-only files so their metadata can be read.
          -e, --ext <list>       Extensions to include, e.g. --ext cbz,cbr,pdf
              --all-files        Consider every file, whatever its extension.
              --lexicon <path>   Extra title lexicon to merge in.
          -y, --yes              Apply without asking. For scripts.
          -n, --dry-run          Show the preview and stop.
          -u, --undo             Put the last run in this folder back and exit.
          -h, --help             This text.
              --version          Version number.

        EXAMPLES
          HumbleRenamer
          HumbleRenamer "D:\Comics\Humble Bundle"
          HumbleRenamer D:\Comics --recurse --online
          HumbleRenamer D:\Comics --template "{Series}[ v{Volume:00}][ ({Year})]"
          HumbleRenamer D:\Comics --undo

        Your own titles can be added to %APPDATA%\HumbleRenamer\lexicon.txt — see the
        [titles] section of the built-in lexicon for the format.
        """;
}
