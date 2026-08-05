namespace HumbleRename.Cli;

/// <summary>
/// The opening screen: a block-letter logo in the BBS/warez ANSI tradition.
/// </summary>
/// <remarks>
/// Drawn with the classic 16-colour console palette rather than 24-bit escapes,
/// which is both period-correct and works in plain conhost as well as Windows
/// Terminal. Block glyphs get the bright colour and the box-drawing glyphs get
/// dark grey, which is what produces the drop-shadow depth.
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

    /// <summary>Face colour per logo row: magenta for HUMBLE, cyan for RENAME.</summary>
    private static readonly ConsoleColor[] RowColors =
    [
        ConsoleColor.DarkMagenta, ConsoleColor.Magenta, ConsoleColor.Magenta,
        ConsoleColor.Magenta, ConsoleColor.Magenta, ConsoleColor.DarkMagenta,
        ConsoleColor.DarkCyan, ConsoleColor.Cyan, ConsoleColor.Cyan,
        ConsoleColor.Cyan, ConsoleColor.Cyan, ConsoleColor.DarkCyan,
    ];

    public static void Show(string version)
    {
        TryClear();
        Console.WriteLine();

        WriteBar();
        Console.WriteLine();

        for (var row = 0; row < Logo.Length; row++)
        {
            Console.Write("     ");
            WriteShadowed(Logo[row], RowColors[row]);
            Console.WriteLine();
        }

        Console.WriteLine();
        WriteBar();

        // Inverted caps: the house style of a thousand .nfo files.
        Console.Write("   ");
        ConsoleUi.Write("· pROPER nAMES fOR hUMBLE bUNDLE cOMICS & bOOKS ·", ConsoleColor.Gray);
        ConsoleUi.Write("   [ ", ConsoleColor.DarkGray);
        ConsoleUi.Write($"v{version}", ConsoleColor.White);
        ConsoleUi.WriteLine(" ]", ConsoleColor.DarkGray);

        WriteBar();
        Console.WriteLine();
    }

    /// <summary>
    /// A three-line banner for menu redraws. The full logo is for the opening screen;
    /// repainting twelve rows on every keystroke would just churn the display.
    /// </summary>
    public static void ShowCompact(string version)
    {
        Console.WriteLine();
        WriteBar();

        ConsoleUi.Write("  ▄▄▄ ", ConsoleColor.Magenta);
        ConsoleUi.Write("H U M B L E", ConsoleColor.White);
        ConsoleUi.Write("  ", ConsoleColor.Gray);
        ConsoleUi.Write("R E N A M E", ConsoleColor.Cyan);
        ConsoleUi.Write(" ▄▄▄", ConsoleColor.Magenta);
        ConsoleUi.Write("   [ ", ConsoleColor.DarkGray);
        ConsoleUi.Write($"v{version}", ConsoleColor.White);
        ConsoleUi.WriteLine(" ]", ConsoleColor.DarkGray);

        WriteBar();
    }

    /// <summary>Draws the shaded divider rule: ░▒▓ ... ▓▒░</summary>
    private static void WriteBar()
    {
        var fill = new string('█', Math.Max(4, Width - 12));

        ConsoleUi.Write("  ░", ConsoleColor.DarkMagenta);
        ConsoleUi.Write("▒", ConsoleColor.Magenta);
        ConsoleUi.Write("▓", ConsoleColor.Magenta);
        ConsoleUi.Write(fill, ConsoleColor.DarkMagenta);
        ConsoleUi.Write("▓", ConsoleColor.Magenta);
        ConsoleUi.Write("▒", ConsoleColor.Magenta);
        ConsoleUi.WriteLine("░", ConsoleColor.DarkMagenta);
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
