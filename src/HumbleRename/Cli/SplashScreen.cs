namespace HumbleRename.Cli;

/// <summary>
/// The opening screen: a scene-style NFO in the Dearmore green-phosphor tradition —
/// a big block-letter logo, a release-info panel, and a fully closed double-line frame.
/// </summary>
/// <remarks>
/// Drawn with the classic 16-colour console palette rather than 24-bit escapes, so it
/// works in plain conhost as well as Windows Terminal. The frame is real box-drawing:
/// corners are joined to their walls by continuous <c>═</c> runs, and every line is the
/// same <see cref="ConsoleUi.FrameInner"/> width, so nothing floats or points astray.
/// </remarks>
public static class SplashScreen
{
    private const ConsoleColor Bright = ConsoleColor.Green;
    private const ConsoleColor Dim = ConsoleColor.DarkGreen;
    private const ConsoleColor Ghost = ConsoleColor.DarkGray;

    /// <summary>Columns between the two side walls. Shared with <see cref="ConsoleUi"/>.</summary>
    private const int Inner = ConsoleUi.FrameInner;

    /// <summary>Glyphs forming the letter faces.</summary>
    private const string BlockGlyphs = "█▀▄▌▐";

    /// <summary>Glyphs forming the drop shadow.</summary>
    private const string ShadowGlyphs = "╗║╝═╚╔╦╩╬╠╣";

    private static readonly string[] Logo =
    [
        @"██╗  ██╗██╗   ██╗███╗   ███╗██████╗ ██╗     ███████╗",
        @"██║  ██║██║   ██║████╗ ████║██╔══██╗██║     ██╔════╝",
        @"███████║██║   ██║██╔████╔██║██████╔╝██║     █████╗  ",
        @"██╔══██║██║   ██║██║╚██╔╝██║██╔══██╗██║     ██╔══╝  ",
        @"██║  ██║╚██████╔╝██║ ╚═╝ ██║██████╔╝███████╗███████╗",
        @"╚═╝  ╚═╝ ╚═════╝ ╚═╝     ╚═╝╚═════╝ ╚══════╝╚══════╝",
        @"██████╗ ███████╗███╗   ██╗ █████╗ ███╗   ███╗███████╗",
        @"██╔══██╗██╔════╝████╗  ██║██╔══██╗████╗ ████║██╔════╝",
        @"██████╔╝█████╗  ██╔██╗ ██║███████║██╔████╔██║█████╗  ",
        @"██╔══██╗██╔══╝  ██║╚██╗██║██╔══██║██║╚██╔╝██║██╔══╝  ",
        @"██║  ██║███████╗██║ ╚████║██║  ██║██║ ╚═╝ ██║███████╗",
        @"╚═╝  ╚═╝╚══════╝╚═╝  ╚═══╝╚═╝  ╚═╝╚═╝     ╚═╝╚══════╝",
    ];

    /// <summary>Face colour per logo row: bright and dim phosphor green.</summary>
    private static readonly ConsoleColor[] RowColors =
    [
        Dim, Bright, Bright, Bright, Bright, Dim,
        Dim, Bright, Bright, Bright, Bright, Dim,
    ];

    public static void Show(string version)
    {
        TryClear();
        Console.WriteLine();

        // Every logo row shares one left offset, so the two words stack into
        // coherent letters instead of drifting when their widths differ.
        var logoIndent = (Inner - MaxWidth(Logo)) / 2;

        Top();
        ScanBar();
        Blank();

        for (var row = 0; row < Logo.Length; row++)
        {
            LogoRow(Logo[row], RowColors[row], logoIndent);
        }

        Blank();
        Centered("« cracking the case of humble's borked filenames »", Bright);
        Blank();

        Cross(null);
        Info("SUPPLIED BY", "humble bundle  ·  drm-free zone");
        Info("RENAME CREW", "viterbi word-split  +  curated lexicon");
        Info("TARGETS", "cbz cbr cb7 cbt  ·  pdf epub mobi azw3");
        Info("SYSTEMS", "windows  ·  linux  ·  wsl");
        Cross(null);

        Centered("RESTORE  ·  PREVIEW  ·  RENAME  ·  UNDO", Bright);
        Centered($"release {version}   //   0-day name fixer   //   100% clean", Ghost);
        Bottom();
        Console.WriteLine();
    }

    /// <summary>
    /// A short banner for menu redraws. The full NFO is for the opening screen;
    /// repainting the whole thing on every keystroke would just churn the display.
    /// </summary>
    public static void ShowCompact(string version)
    {
        Console.WriteLine();
        Top();
        Wordmark("H U M B L E   R E N A M E R");
        Centered($"file liberation unit   //   release {version}", Bright);
        Bottom();
    }

    // ── Frame pieces ────────────────────────────────────────────────────────

    private static void Top() => Rule('╔', '╗', null);

    private static void Bottom() => Rule('╚', '╝', null);

    /// <summary>An interior crossbar, optionally carrying an inset title.</summary>
    private static void Cross(string? title) => Rule('╠', '╣', title);

    private static void Rule(char left, char right, string? title)
    {
        ConsoleUi.Write("  ", Dim);
        ConsoleUi.Write(left.ToString(), Dim);

        if (string.IsNullOrEmpty(title))
        {
            ConsoleUi.Write(new string('═', Inner), Dim);
        }
        else
        {
            var upper = title.ToUpperInvariant();
            ConsoleUi.Write("══[ ", Dim);
            ConsoleUi.Write(upper, Bright);
            ConsoleUi.Write(" ]", Dim);
            ConsoleUi.Write(new string('═', Math.Max(0, Inner - upper.Length - 6)), Dim);
        }

        ConsoleUi.WriteLine(right.ToString(), Dim);
    }

    /// <summary>An empty interior row with intact side walls.</summary>
    private static void Blank()
    {
        ConsoleUi.Write("  ║", Dim);
        ConsoleUi.Write(new string(' ', Inner), Dim);
        ConsoleUi.WriteLine("║", Dim);
    }

    /// <summary>A centred wordmark: caps flanked by phosphor wings, lit white.</summary>
    private static void Wordmark(string text)
    {
        const string wingLeft = "▓▒░  ";
        const string wingRight = "  ░▒▓";
        var span = wingLeft.Length + text.Length + wingRight.Length;
        var left = (Inner - span) / 2;

        ConsoleUi.Write("  ║", Dim);
        ConsoleUi.Write(new string(' ', left), Dim);
        ConsoleUi.Write(wingLeft, Dim);
        ConsoleUi.Write(text, ConsoleColor.White);
        ConsoleUi.Write(wingRight, Dim);
        ConsoleUi.Write(new string(' ', Inner - left - span), Dim);
        ConsoleUi.WriteLine("║", Dim);
    }

    /// <summary>A glowing phosphor bar — a CRT scanline bloom, brightest at the centre.</summary>
    private static void ScanBar()
    {
        const string ramp = "░▒▓█▓▒░";
        ConsoleUi.Write("  ║", Dim);
        for (var index = 0; index < Inner; index++)
        {
            var position = (double)index / (Inner - 1);
            var distance = Math.Abs(position - 0.5) * 2;
            var color = distance switch
            {
                < 0.34 => Bright,
                < 0.70 => Dim,
                _ => Ghost,
            };
            ConsoleUi.Write(ramp[index % ramp.Length].ToString(), color);
        }

        ConsoleUi.WriteLine("║", Dim);
    }

    /// <summary>A centred single-colour caption row.</summary>
    private static void Centered(string text, ConsoleColor color)
    {
        var clipped = text.Length > Inner ? text[..Inner] : text;
        var left = (Inner - clipped.Length) / 2;

        ConsoleUi.Write("  ║", Dim);
        ConsoleUi.Write(new string(' ', left), Dim);
        ConsoleUi.Write(clipped, color);
        ConsoleUi.Write(new string(' ', Inner - left - clipped.Length), Dim);
        ConsoleUi.WriteLine("║", Dim);
    }

    /// <summary>A release-panel line: a bright label, a dotted leader, and a value.</summary>
    private static void Info(string label, string value)
    {
        const int leader = 15;
        var dots = Math.Max(2, leader - label.Length);

        ConsoleUi.Write("  ║   ", Dim);
        ConsoleUi.Write(label + " ", Bright);
        ConsoleUi.Write(new string('·', dots) + " ", Dim);
        ConsoleUi.Write(value, ConsoleColor.Gray);

        var used = 3 + label.Length + 1 + dots + 1 + value.Length;
        ConsoleUi.Write(new string(' ', Math.Max(1, Inner - used)), Dim);
        ConsoleUi.WriteLine("║", Dim);
    }

    /// <summary>One logo row, centred on the shared indent and drawn with a drop shadow.</summary>
    private static void LogoRow(string line, ConsoleColor faceColor, int indent)
    {
        ConsoleUi.Write("  ║", Dim);
        ConsoleUi.Write(new string(' ', indent), Dim);
        WriteShadowed(line, faceColor);
        ConsoleUi.Write(new string(' ', Math.Max(0, Inner - indent - line.Length)), Dim);
        ConsoleUi.WriteLine("║", Dim);
    }

    private static int MaxWidth(IEnumerable<string> lines)
    {
        var max = 0;
        foreach (var line in lines)
        {
            if (line.Length > max)
            {
                max = line.Length;
            }
        }

        return max;
    }

    /// <summary>
    /// Writes one logo row, colouring letter faces and drop shadow separately so the
    /// glyphs read as raised blocks lit from the left rather than a flat wall of colour.
    /// </summary>
    private static void WriteShadowed(string line, ConsoleColor faceColor)
    {
        for (var index = 0; index < line.Length; index++)
        {
            var glyph = line[index];
            if (BlockGlyphs.Contains(glyph))
            {
                var normalized = line.Length <= 1 ? 0.5 : (double)index / (line.Length - 1);
                var distance = Math.Abs(normalized - 0.5) * 2;
                var color = distance switch
                {
                    < 0.18 => ConsoleColor.White,
                    < 0.48 => Bright,
                    _ => faceColor,
                };
                ConsoleUi.Write(glyph.ToString(), color);
            }
            else if (ShadowGlyphs.Contains(glyph))
            {
                ConsoleUi.Write(glyph.ToString(), Ghost);
            }
            else
            {
                ConsoleUi.Write(glyph.ToString(), Dim);
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
