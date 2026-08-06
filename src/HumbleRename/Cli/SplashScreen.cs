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

        // Compact product line beneath the mark.
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

        ConsoleUi.Write("  ┌─ ", ConsoleColor.DarkGreen);
        ConsoleUi.Write("H U M B L E", ConsoleColor.Green);
        ConsoleUi.Write("  ·  ", ConsoleColor.DarkGray);
        ConsoleUi.Write("R E N A M E", ConsoleColor.Green);
        ConsoleUi.Write(" ─┐", ConsoleColor.DarkGreen);
        ConsoleUi.Write("   [ ", ConsoleColor.DarkGray);
        ConsoleUi.Write($"v{version}", ConsoleColor.White);
        ConsoleUi.WriteLine(" ]", ConsoleColor.DarkGray);

        WriteBar();
    }

    /// <summary>Draws the dim phosphor divider used by the Dearmore-style shell.</summary>
    private static void WriteBar()
    {
        var fill = new string('═', Math.Max(4, Width - 8));
        ConsoleUi.Write("  ╔", ConsoleColor.DarkGreen);
        ConsoleUi.Write(fill, ConsoleColor.DarkGreen);
        ConsoleUi.WriteLine("╗", ConsoleColor.DarkGreen);
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
