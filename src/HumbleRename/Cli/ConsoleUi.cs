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
    // Dearmore.net's green-phosphor terminal palette, mapped to the portable
    // 16-colour console set. Consoles cannot reproduce CSS scanlines or glow,
    // but the hierarchy stays intact in Windows Terminal and conhost.
    private const ConsoleColor Structural = ConsoleColor.Green;
    private const ConsoleColor Action = ConsoleColor.Green;
    private const ConsoleColor Panel = ConsoleColor.DarkGreen;
    private const ConsoleColor Body = ConsoleColor.Gray;

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

        try
        {
            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = Body;
        }
        catch (IOException)
        {
            // Cosmetic only; some hosts do not expose console colours.
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
        Write("  ▓▒░ ", ConsoleColor.DarkGreen);
        Write(text, Structural);
        WriteLine(" ░▒▓", ConsoleColor.DarkGreen);
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
                    WriteLine(action.OriginalName, ConsoleColor.DarkGreen);
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

    /// <summary>
    /// Reads a full keypress, so callers can tell the arrow keys apart. Under redirected
    /// input a line stands in: empty is Enter, and ',' / '.' stand in for the Left /
    /// Right arrows so the hand-review flow stays scriptable in tests.
    /// </summary>
    public static ConsoleKeyInfo ReadKeyInfo()
    {
        if (!Console.IsInputRedirected)
        {
            return Console.ReadKey(intercept: true);
        }

        var line = Console.ReadLine();
        if (line is null)
        {
            // End of scripted input: behave like "finish" so nothing spins.
            return new ConsoleKeyInfo('Q', ConsoleKey.Q, false, false, false);
        }

        var trimmed = line.Trim();
        return trimmed.Length == 0
            ? new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false)
            : new ConsoleKeyInfo(trimmed[0], default, false, false, false);
    }

    // ── Frame geometry ──────────────────────────────────────────────────────
    // Every framed line is exactly 80 columns wide: a two-space gutter, a
    // vertical bar, 76 inner columns, and a closing bar. Rules, rows, and
    // dividers all derive their padding from this one number, so paired corners
    // and side walls always land in the same column and the box reads as closed.
    internal const int FrameInner = 76;

    // The widest a content row may be after its leading space: inner columns, less
    // the leading and trailing margins and at least one space before the wall.
    private const int MaxContent = FrameInner - 3;

    /// <summary>Draws the top edge of a framed section: a titled double-line rule.</summary>
    public static void Section(string title)
    {
        Console.WriteLine();
        FrameRule('╔', '╗', '═', title.ToUpperInvariant(), Structural);
    }

    /// <summary>
    /// Draws one menu row: a bracketed key, a dotted leader, and the current value.
    /// </summary>
    public static void MenuItem(string key, string label, string? value = null, int labelWidth = 24)
    {
        FrameRowStart();
        Write("[", Panel);
        Write(key, Action);
        Write("]  ", Panel);
        var used = 4 + key.Length; // "[" + key + "]  "

        if (value is null)
        {
            var text = FitHead(label, MaxContent - used);
            Write(text, Body);
            FrameRowEnd(used + text.Length);
            return;
        }

        Write(label + " ", Body);
        var dots = Math.Max(1, labelWidth - label.Length);
        Write(new string('·', dots) + " ", ConsoleColor.DarkGreen);

        // A long value — most often a folder path — is truncated from the left so
        // the informative tail survives and the right wall stays put.
        var prefix = used + label.Length + 1 + dots + 1;
        var shown = FitTail(value, Math.Max(1, MaxContent - prefix));
        Write(shown, Structural);
        FrameRowEnd(prefix + shown.Length);
    }

    /// <summary>
    /// A framed selectable list row: a selection caret, a bracketed key, and a label.
    /// Used for the preset pickers so the whole list stays inside the frame.
    /// </summary>
    public static void MenuChoice(string key, string label, bool selected)
    {
        FrameRowStart();
        Write(selected ? "> [" : "  [", selected ? Structural : Panel);
        Write(key, Action);
        Write("]  ", Panel);
        var text = FitHead(label, MaxContent - 6 - key.Length); // "> [" + key + "]  "
        Write(text, selected ? ConsoleColor.White : Body);
        FrameRowEnd(6 + key.Length + text.Length);
    }

    /// <summary>A framed, dimmed sub-line indented to sit under a <see cref="MenuChoice"/> label.</summary>
    public static void MenuNote(string text)
    {
        const int indent = 7; // lands under the label column of MenuChoice
        FrameRowStart();
        Write(new string(' ', indent), Panel);
        // Keep the tail so a truncated filename still shows its extension.
        var shown = FitTail(text, MaxContent - indent);
        Write(shown, ConsoleColor.DarkGray);
        FrameRowEnd(indent + shown.Length);
    }

    /// <summary>Separates primary actions from configuration with a light inner shelf.</summary>
    public static void MenuDivider() => FrameRule('╟', '╢', '─', null, Panel);

    /// <summary>Draws the bottom edge of a framed section plus the input caret.</summary>
    public static void Prompt(string label)
    {
        FrameRule('╚', '╝', '═', label.ToUpperInvariant(), Structural);
        Write("  ", Panel);
        Write("░▒▓", ConsoleColor.DarkGreen);
        Write("█▓▒", ConsoleColor.Green);
        Write(" ▶ ", Action);
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

    /// <summary>Opens a framed content row: gutter, left wall, one leading space.</summary>
    private static void FrameRowStart() => Write("  ║ ", Panel);

    /// <summary>
    /// Closes a framed content row, padding so the right wall lands in column 80.
    /// <paramref name="contentColumns"/> is everything written after the leading space.
    /// </summary>
    private static void FrameRowEnd(int contentColumns)
    {
        var padding = Math.Max(1, FrameInner - 2 - contentColumns);
        Write(new string(' ', padding), Panel);
        WriteLine(" ║", Panel);
    }

    /// <summary>
    /// A full-width horizontal rule drawn between two corner or junction glyphs,
    /// with an optional inset title cartouche. The run between the corners is a
    /// continuous line of <paramref name="fill"/>, so the corners actually join
    /// the walls instead of floating.
    /// </summary>
    private static void FrameRule(char left, char right, char fill, string? title, ConsoleColor titleColor)
    {
        Write("  ", Panel);
        Write(left.ToString(), Panel);

        if (string.IsNullOrEmpty(title))
        {
            Write(new string(fill, FrameInner), Panel);
        }
        else
        {
            // Keep at least two fill glyphs on the right so the closing corner never
            // gets shoved out past column 80 by an over-long title.
            var text = FitHead(title, FrameInner - 8);
            Write($"{fill}{fill}[ ", Panel);
            Write(text, titleColor);
            Write(" ]", Panel);
            var used = 6 + text.Length; // two fills + "[ " + title + " ]"
            Write(new string(fill, Math.Max(0, FrameInner - used)), Panel);
        }

        WriteLine(right.ToString(), Panel);
    }

    /// <summary>Clamps <paramref name="text"/> to <paramref name="max"/> columns, keeping the
    /// head and marking the cut with a trailing ellipsis.</summary>
    private static string FitHead(string text, int max)
    {
        if (max < 1)
        {
            return string.Empty;
        }

        if (text.Length <= max)
        {
            return text;
        }

        return max == 1 ? "…" : text[..(max - 1)] + "…";
    }

    /// <summary>Clamps <paramref name="text"/> to <paramref name="max"/> columns, keeping the
    /// tail (the end of a path) and marking the cut with a leading ellipsis.</summary>
    private static string FitTail(string text, int max)
    {
        if (max < 1)
        {
            return string.Empty;
        }

        if (text.Length <= max)
        {
            return text;
        }

        return max == 1 ? "…" : "…" + text[(text.Length - (max - 1))..];
    }
}
