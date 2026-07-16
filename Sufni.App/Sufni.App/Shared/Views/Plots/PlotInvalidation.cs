using System;

namespace Sufni.App.Shared.Views.Plots;

[Flags]
public enum PlotInvalidation
{
    None = 0,
    Cursor = 1 << 0,
    Overlay = 1 << 1,
    Data = 1 << 2,
    Viewport = 1 << 3,
    Layout = 1 << 4,
    Theme = 1 << 5,
    All = Cursor | Overlay | Data | Viewport | Layout | Theme,
}
