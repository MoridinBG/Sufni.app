using Avalonia.Media;

namespace Sufni.App.Theming;

// Primitive named colors for the dark theme. Raw hex literals live here only;
// SufniDarkTheme composes these into semantic role tokens.
public static class SufniDarkPalette
{
    private static Color C(string hex) => Color.Parse(hex);

    // Whites and light cool grays (used for text and high-contrast plot marks).
    public static readonly Color PureWhite = C("#FEFEFE");
    public static readonly Color Snow      = C("#F0F0F0");
    public static readonly Color Mist      = C("#DDDDDD");
    public static readonly Color SlateMist = C("#D6DDE4");
    public static readonly Color Fog       = C("#D0D0D0");
    public static readonly Color Pearl     = C("#C8C8C8");
    public static readonly Color Silver    = C("#C0C0C0");
    public static readonly Color Ash       = C("#A0A0A0");

    // Neutral mid grays (lines, dividers, disabled text).
    public static readonly Color Graphite = C("#606060");
    public static readonly Color Iron     = C("#5A5A5A");
    public static readonly Color DarkIron = C("#505050");
    public static readonly Color Charcoal = C("#404040");
    public static readonly Color Coal     = C("#303030");

    // Cool slate grays (graph connectors, grid, subtle lines).
    public static readonly Color SlateGray     = C("#63727A");
    public static readonly Color SteelGray     = C("#505558");
    public static readonly Color Gunmetal      = C("#3A3F42");
    public static readonly Color SlateCharcoal = C("#343C42");

    // Deep slates (page surfaces, inputs, hover/selection chrome).
    public static readonly Color Slate         = C("#2C3032");
    public static readonly Color SlateNight    = C("#272D33");
    public static readonly Color SlateMidnight = C("#25292C");
    public static readonly Color SlateAbyss    = C("#20262B");
    public static readonly Color SlateBlack    = C("#1A1F23");
    public static readonly Color Obsidian      = C("#15191C");
    public static readonly Color Onyx          = C("#10161B");

    // Hosted graph-row depth ramp, level 1 (lightest of the three deeps).
    public static readonly Color TarContainer = C("#0F1314");
    public static readonly Color TarHeader    = C("#101416");
    public static readonly Color TarFigure    = C("#101518");
    public static readonly Color TarData      = C("#1B2126");

    // Hosted graph-row depth ramp, level 2.
    public static readonly Color PitchContainer = C("#0A0C0D");
    public static readonly Color PitchHeader    = C("#07090A");
    public static readonly Color PitchFigure    = C("#0A0D0F");
    public static readonly Color PitchData      = C("#11161A");

    // Hosted graph-row depth ramp, level 3+ (deepest, near-black).
    public static readonly Color VoidContainer = C("#050607");
    public static readonly Color VoidHeader    = C("#030404");
    public static readonly Color VoidFigure    = C("#050708");
    public static readonly Color VoidData      = C("#0B0F12");

    // Brand accent (primary action, focus ring, hyperlinks, etc.).
    public static readonly Color AccentBlue      = C("#0078D7");
    public static readonly Color AccentBlueLight = C("#1E8AE0");

    // Status colors.
    public static readonly Color DangerRed     = C("#BF312D");
    public static readonly Color DangerRedDark = C("#9F110D");
    public static readonly Color WarningGold   = C("#DAA520");

    // Drag-and-drop chrome (blue-tinted dark teals).
    public static readonly Color DragHeader        = C("#263238");
    public static readonly Color DropTargetTeal    = C("#1F3A46");

    // Plot markers and dataviz reds (theme-local, distinct from SignalSeries).
    public static readonly Color MarkerBlue = C("#56B4E9");
    public static readonly Color MarkerRed  = C("#D53E4F");
}
