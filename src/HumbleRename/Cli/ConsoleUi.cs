using HumbleRename.Renaming;

namespace HumbleRename.Cli;

/// <summary>
/// All terminal output and prompting.
/// </summary>
/// <remarks>
/// Uses <see cref="Console.ForegroundColor"/> rather than ANSI escapes so the output
/// behaves in plain conhost as well as Windows Terminal. Colour is suppressed when
/// output is redirected or NO_COLOR is set.
/// </remarks>
public static class ConsoleUi
{
    private static readonly bool UseColor =
        !Console.IsOutputRedirected &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));

    /// <summary>
    /// Switches the console to UTF-8 so accented titles survive the trip to the screen.
    /// The default Windows code page renders them as mojibake.
    /// </summary>
    public static void EnableUnicodeOutput()
    {
        try
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
        }
        catch (Exception ex) when (ex is IOException or PlatformNotSupportedException)
        {
            // Some hosts refuse; ASCII output still works.
        }
    }

    /// <summary>True when we can ask the user something and expect an answer.</summary>
    public static bool IsInteractive => !Console.IsInputRedirected;

    public static void Write(string text, ConsoleColor color)
    {
        if (!UseColor)
        {
            Console.Write(text);
            return;
        }

        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = previous;
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    public static void Heading(string text)
    {
        Console.WriteLine();
        WriteLine(text, ConsoleColor.Cyan);
        WriteLine(new string('-', Math.Min(text.Length, SafeWidth())), ConsoleColor.DarkGray);
    }

    public static void Error(string text) => WriteLine(text, ConsoleColor.Red);

    public static void Warn(string text) => WriteLine(text, ConsoleColor.Yellow);

    public static void Muted(string text) => WriteLine(text, ConsoleColor.DarkGray);

    /// <summary>
    /// Prints every file's current name and what it would become. Renames are shown
    /// as a pair of lines so long comic titles are never truncated.
    /// </summary>
    public static void WritePlan(RenamePlan plan, bool showUnchanged = true)
    {
        ArgumentNullException.ThrowIfNull(plan);

        Heading($"Preview - {plan.Root}");

        var index = 0;
        var width = plan.Actions.Count.ToString().Length;

        foreach (var action in plan.Actions)
        {
            index++;

            if (action.Status == RenameStatus.Unchanged && !showUnchanged)
            {
                continue;
            }

            var number = index.ToString().PadLeft(width);

            switch (action.Status)
            {
                case RenameStatus.Rename:
                case RenameStatus.Deduplicated:
                    Console.Write($"  {number}  ");
                    WriteLine(action.OriginalName, ConsoleColor.DarkYellow);
                    Console.Write(new string(' ', width + 3));
                    Write("-> ", ConsoleColor.DarkGray);
                    WriteLine(action.ProposedName, ConsoleColor.Green);
                    break;

                case RenameStatus.Unchanged:
                    Console.Write($"  {number}  ");
                    Write(action.OriginalName, ConsoleColor.DarkGray);
                    WriteLine("  (already correct)", ConsoleColor.DarkGray);
                    break;

                case RenameStatus.Skipped:
                case RenameStatus.Error:
                    Console.Write($"  {number}  ");
                    Write(action.OriginalName, ConsoleColor.DarkGray);
                    WriteLine($"  (skipped: {action.Note})", ConsoleColor.DarkRed);
                    break;
            }

            if (!string.IsNullOrEmpty(action.Note) && action.Status is RenameStatus.Rename
                    or RenameStatus.Deduplicated or RenameStatus.Unchanged)
            {
                Console.Write(new string(' ', width + 3));
                Muted($"   {action.Note}");
            }
        }

        Console.WriteLine();
        WriteSummary(plan);
    }

    private static void WriteSummary(RenamePlan plan)
    {
        Write($"  {plan.Actions.Count} file(s) scanned", ConsoleColor.Gray);
        Write("  |  ", ConsoleColor.DarkGray);
        Write($"{plan.ChangeCount} to rename", ConsoleColor.Green);
        Write("  |  ", ConsoleColor.DarkGray);
        Write($"{plan.UnchangedCount} already correct", ConsoleColor.DarkGray);

        if (plan.SkippedCount > 0)
        {
            Write("  |  ", ConsoleColor.DarkGray);
            Write($"{plan.SkippedCount} skipped", ConsoleColor.DarkRed);
        }

        Console.WriteLine();
    }

    /// <summary>
    /// Asks a yes/no question. Returns <paramref name="defaultAnswer"/> when input is
    /// redirected, so scripted runs never hang waiting for a keypress.
    /// </summary>
    public static bool Confirm(string question, bool defaultAnswer = false)
    {
        if (!IsInteractive)
        {
            return defaultAnswer;
        }

        var hint = defaultAnswer ? "[Y/n]" : "[y/N]";

        while (true)
        {
            Console.WriteLine();
            Write($"{question} {hint} ", ConsoleColor.White);

            var answer = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(answer))
            {
                return defaultAnswer;
            }

            switch (answer.ToLowerInvariant())
            {
                case "y" or "yes":
                    return true;
                case "n" or "no":
                    return false;
                default:
                    Warn("  Please answer y or n.");
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a single menu selection. Uses an unbuffered keypress when a real console
    /// is attached so menus respond instantly, and falls back to a line read otherwise.
    /// </summary>
    public static char ReadChoice()
    {
        if (Console.IsInputRedirected)
        {
            var line = Console.ReadLine();
            return string.IsNullOrWhiteSpace(line) ? '\0' : char.ToUpperInvariant(line.Trim()[0]);
        }

        var key = Console.ReadKey(intercept: true);
        return char.ToUpperInvariant(key.KeyChar);
    }

    /// <summary>Draws a warez-style section rule: ▓▒░ TITLE ░▒▓────────</summary>
    public static void Section(string title)
    {
        Console.WriteLine();
        Write("  ▓▒░ ", ConsoleColor.Magenta);
        Write(title.ToUpperInvariant(), ConsoleColor.White);
        Write(" ░▒▓", ConsoleColor.Magenta);

        var used = 6 + title.Length + 4;
        var remaining = Math.Max(0, Math.Min(SafeWidth() - 2, 78) - used);
        WriteLine(new string('─', remaining), ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    /// <summary>
    /// Draws one menu row: a bracketed key, a dotted leader, and the current value.
    /// </summary>
    public static void MenuItem(string key, string label, string? value = null, int labelWidth = 24)
    {
        Write("   [", ConsoleColor.DarkGray);
        Write(key, ConsoleColor.Yellow);
        Write("]  ", ConsoleColor.DarkGray);

        if (value is null)
        {
            WriteLine(label, ConsoleColor.Gray);
            return;
        }

        Write(label + " ", ConsoleColor.Gray);
        var dots = Math.Max(1, labelWidth - label.Length);
        Write(new string('.', dots) + " ", ConsoleColor.DarkGray);
        WriteLine(value, ConsoleColor.Cyan);
    }

    /// <summary>Draws the prompt the user types their menu selection at.</summary>
    public static void Prompt(string label)
    {
        Console.WriteLine();
        Write("  ▓▒░ ", ConsoleColor.Magenta);
        Write(label, ConsoleColor.White);
        Write(" ░▒▓ ", ConsoleColor.Magenta);
        Write("> ", ConsoleColor.Yellow);
    }

    public static void TryClear()
    {
        try
        {
            if (!Console.IsOutputRedirected)
            {
                Console.Clear();
            }
        }
        catch (IOException)
        {
            // Cosmetic only.
        }
    }

    /// <summary>Waits for any key before returning to a menu.</summary>
    public static void Pause(string message = "press any key to continue")
    {
        Console.WriteLine();
        Muted($"  {message}...");

        if (Console.IsInputRedirected)
        {
            Console.ReadLine();
            return;
        }

        Console.ReadKey(intercept: true);
    }

    /// <summary>Asks for the folder to work on, as the original tool did.</summary>
    public static string? PromptForFolder()
    {
        if (!IsInteractive)
        {
            return null;
        }

        Write("Folder to rename: ", ConsoleColor.White);
        var input = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        // Tolerate a path pasted with surrounding quotes.
        return input.Trim('"').Trim();
    }

    /// <summary>Single-line progress that overwrites itself.</summary>
    public static void Progress(int done, int total, string current)
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        var width = SafeWidth();
        var label = $"  Scanning {done}/{total}  {current}";

        if (label.Length > width - 1)
        {
            label = label[..(width - 2)];
        }

        Console.Write('\r' + label.PadRight(width - 1));
    }

    public static void ClearProgress()
    {
        if (Console.IsOutputRedirected)
        {
            return;
        }

        Console.Write('\r' + new string(' ', SafeWidth() - 1) + '\r');
    }

    private static int SafeWidth()
    {
        try
        {
            return Math.Max(40, Console.WindowWidth);
        }
        catch (IOException)
        {
            return 80;
        }
    }
}
