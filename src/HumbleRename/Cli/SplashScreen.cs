namespace HumbleRename.Cli;

/// <summary>
/// The opening screen: a block-letter logo in the Dearmore green-phosphor tradition.
/// </summary>
/// <remarks>
/// Drawn with the classic 16-colour console palette rather than 24-bit escapes,
/// so it works in plain conhost as well as Windows Terminal. Green is structural,
/// with a dim green frame to echo the web app's phosphor hierarchy.
/// </remarks>
public static class SplashScreen
{
    /// <summary>Glyphs forming the letter faces.</summary>
    private const string BlockGlyphs = "█▀▄▌▐";

    /// <summary>Glyphs forming the drop shadow.</summary>
    private const string ShadowGlyphs = "╗║╝═╚╔╦╩╬╠╣";

    private const int Width = 78;

    private static readonly string[] Logo =
    [
        @"██╗  ██╗██╗   ██╗███╗   ███╗██████╗ ██╗     ███████╗",
        @"██║  ██║██║   ██║████╗ ████║██╔══██╗██║     ██╔════╝",
        @"███████║██║   ██║██╔████╔██║██████╔╝██║     █████╗",
        @"██╔══██║██║   ██║██║╚██╔╝██║██╔══██╗██║     ██╔══╝",
        @"██║  ██║╚██████╔╝██║ ╚═╝ ██║██████╔╝███████╗███████╗",
        @"╚═╝  ╚═╝ ╚═════╝ ╚═╝     ╚═╝╚═════╝ ╚══════╝╚══════╝",
        @"██████╗ ███████╗███╗   ██╗ █████╗ ███╗   ███╗███████╗",
        @"██╔══██╗██╔════╝████╗  ██║██╔══██╗████╗ ████║██╔════╝",
        @"██████╔╝█████╗  ██╔██╗ ██║███████║██╔████╔██║█████╗",
        @"██╔══██╗██╔══╝  ██║╚██╗██║██╔══██║██║╚██╔╝██║██╔══╝",
        @"██║  ██║███████╗██║ ╚████║██║  ██║██║ ╚═╝ ██║███████╗",
        @"╚═╝  ╚═╝╚══════╝╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝",
    ];

    /// <summary>Face colour per logo row: bright and dim phosphor green.</summary>
    private static readonly ConsoleColor[] RowColors =
    [
        ConsoleColor.DarkGreen, ConsoleColor.Green, ConsoleColor.Green,
        ConsoleColor.Green, ConsoleColor.Green, ConsoleColor.DarkGreen,
        ConsoleColor.DarkGreen, ConsoleColor.Green, ConsoleColor.Green,
        ConsoleColor.Green, ConsoleColor.Green, ConsoleColor.DarkGreen,
    ];

    public static void Show(string version)
    {
        TryClear();
        Console.WriteLine();

        WriteTop("HUMBLE RENAMER // FILE LIBERATION UNIT");
        WriteFrameRow("CRACKING THE CASE OF HUMBLE'S BORKED FILENAMES", ConsoleColor.DarkGreen);
        WriteDivider();

        for (var row = 0; row < Logo.Length; row++)
        {
            ConsoleUi.Write("  ║   ", ConsoleColor.DarkGreen);
            WriteShadowed(Logo[row], RowColors[row]);
            WriteRightEdge(Logo[row].Length + 3);
            Console.WriteLine();
        }

        WriteDivider();
        WriteFrameRow("RESTORE · PREVIEW · RENAME · UNDO", ConsoleColor.Green);
        WriteFrameRow($"RELEASE {version}  //  0-DAY NAME FIXER", ConsoleColor.Gray);
        WriteBottom();
        Console.WriteLine();
    }

    /// <summary>
    /// A three-line banner for menu redraws. The full logo is for the opening screen;
    /// repainting twelve rows on every keystroke would just churn the display.
    /// </summary>
    public static void ShowCompact(string version)
    {
        Console.WriteLine();
        WriteTop("H U M B L E  //  R E N A M E R");
        WriteFrameRow($"FILE LIBERATION UNIT  //  RELEASE {version}", ConsoleColor.Green);
        WriteBottom();
    }

    private static void WriteTop(string label)
    {
        ConsoleUi.Write("  ╔═[ ", ConsoleColor.DarkGreen);
        ConsoleUi.Write(label, ConsoleColor.Green);
        ConsoleUi.Write(" ]", ConsoleColor.DarkGreen);
        var used = 2 + 4 + label.Length + 2 + 1;
        ConsoleUi.Write(new string('═', Math.Max(2, Width - used)), ConsoleColor.DarkGreen);
        ConsoleUi.WriteLine("╗", ConsoleColor.DarkGreen);
    }

    private static void WriteDivider()
    {
        ConsoleUi.Write("  ╠", ConsoleColor.DarkGreen);
        ConsoleUi.Write(new string('═', Width - 2), ConsoleColor.DarkGreen);
        ConsoleUi.WriteLine("╣", ConsoleColor.DarkGreen);
    }

    private static void WriteBottom()
    {
        ConsoleUi.Write("  ╚", ConsoleColor.DarkGreen);
        ConsoleUi.Write(new string('═', Width - 2), ConsoleColor.DarkGreen);
        ConsoleUi.WriteLine("╝", ConsoleColor.DarkGreen);
    }

    private static void WriteFrameRow(string text, ConsoleColor color)
    {
        ConsoleUi.Write("  ║ ", ConsoleColor.DarkGreen);
        ConsoleUi.Write(text, color);
        WriteRightEdge(text.Length + 1);
        Console.WriteLine();
    }

    private static void WriteRightEdge(int used)
    {
        ConsoleUi.Write(new string(' ', Math.Max(1, Width - 4 - used)), ConsoleColor.DarkGreen);
        ConsoleUi.Write("║", ConsoleColor.DarkGreen);
    }

    /// <summary>
    /// Writes one logo row, colouring letter faces and drop shadow separately so the
    /// glyphs read as raised blocks rather than a flat wall of colour.
    /// </summary>
    private static void WriteShadowed(string line, ConsoleColor faceColor)
    {
        foreach (var glyph in line)
        {
            if (BlockGlyphs.Contains(glyph))
            {
                ConsoleUi.Write(glyph.ToString(), faceColor);
            }
            else if (ShadowGlyphs.Contains(glyph))
            {
                ConsoleUi.Write(glyph.ToString(), ConsoleColor.DarkGray);
            }
            else
            {
                Console.Write(glyph);
            }
        }
    }

    private static void TryClear()
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
            // Clearing is cosmetic; some hosts do not allow it.
        }
    }
}
