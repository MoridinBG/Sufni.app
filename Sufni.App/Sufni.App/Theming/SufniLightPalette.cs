using Avalonia.Media;

namespace Sufni.App.Theming;

// Primitive named colors for the light theme. Raw hex literals live here only;
// SufniLightTheme composes these into semantic role tokens.
public static class SufniLightPalette
{
    private static Color C(string hex) => Color.Parse(hex);

    // Cool whites and pale tints (page, elevated, input surfaces).
    public static readonly Color Porcelain = C("#F2F4F6");
    public static readonly Color Linen     = C("#EDEFF2");
    public static readonly Color Vapor     = C("#E8EDF2");
    public static readonly Color Cloud     = C("#E4E8EC");
    public static readonly Color Frost     = C("#E0E4E8");
    public static readonly Color Haze      = C("#DDE1E5");
    public static readonly Color Alabaster = C("#D8DCE0");
    public static readonly Color Smoke     = C("#D6DADF");

    // Mid grays (lines, dividers, borders, plot grid major).
    public static readonly Color Mineral   = C("#D5DAE0");
    public static readonly Color Limestone = C("#CFD3D7");
    public static readonly Color Stone     = C("#C8CCD0");
    public static readonly Color Pewter    = C("#B0B5BA");
    public static readonly Color SlateDark = C("#9CA0A4");

    // Dark grays and near-blacks (text and high-contrast plot marks).
    public static readonly Color Steel      = C("#9AA0A6");
    public static readonly Color Anthracite = C("#5A6068");
    public static readonly Color Tungsten   = C("#3A3F45");
    public static readonly Color Ink        = C("#22272D");
    public static readonly Color IronInk    = C("#1A1F24");
    public static readonly Color Jet        = C("#0A0E12");

    // Selection (blue-tinted pale).
    public static readonly Color SelectionBlue = C("#DCE6F2");

    // Drag-and-drop chrome (blue-tinted pales).
    public static readonly Color Glacier        = C("#D8E5EC");
    public static readonly Color DropTargetBlue = C("#C8DBEA");

    // Hosted graph-row depth ramp, level 1 (Container slot reuses Pearl).
    public static readonly Color SandHeader = C("#D4D8DC");
    public static readonly Color SandFigure = C("#DCE0E4");
    public static readonly Color SandData   = C("#DCE2E8");

    // Hosted graph-row depth ramp, level 2 (Header slot reuses Stone).
    public static readonly Color ClayContainer = C("#CCD0D4");
    public static readonly Color ClayFigure    = C("#D0D4D8");
    public static readonly Color ClayData      = C("#D4DAE0");

    // Hosted graph-row depth ramp, level 3+ (darkest hosted surfaces).
    public static readonly Color EarthContainer = C("#C0C4C8");
    public static readonly Color EarthHeader    = C("#BCC0C4");
    public static readonly Color EarthFigure    = C("#C4C8CC");
    public static readonly Color EarthData      = C("#CAD0D6");

    // Brand accent (primary action, focus ring, hyperlinks, etc.).
    public static readonly Color AccentBlue      = C("#0078D7");
    public static readonly Color AccentBlueLight = C("#1E8AE0");

    // Status colors (WarningOchre is a darker, more legible gold on light surfaces).
    public static readonly Color DangerRed     = C("#BF312D");
    public static readonly Color DangerRedDark = C("#9F110D");
    public static readonly Color WarningOchre  = C("#B7861A");

    // Plot markers and dataviz reds (theme-local, distinct from SignalSeries).
    public static readonly Color MarkerBlue = C("#56B4E9");
    public static readonly Color MarkerRed  = C("#D53E4F");
}
