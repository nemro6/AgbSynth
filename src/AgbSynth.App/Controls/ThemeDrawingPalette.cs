using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace AgbSynth.App.Controls;

internal static class ThemeDrawingPalette
{
    private static readonly IBrush DarkCanvas = Brush("#111111");
    private static readonly IBrush LightCanvas = Brush("#FFFFFF");
    private static readonly IBrush DarkSurface = Brush("#242424");
    private static readonly IBrush LightSurface = Brush("#F3F3F3");
    private static readonly IBrush DarkControl = Brush("#3C3C3C");
    private static readonly IBrush LightControl = Brush("#E1E1E1");
    private static readonly IBrush DarkBorder = Brush("#4A4A4A");
    private static readonly IBrush LightBorder = Brush("#B8B8B8");
    private static readonly IBrush DarkGrid = Brush("#343434");
    private static readonly IBrush LightGrid = Brush("#D2D2D2");
    private static readonly IBrush DarkText = Brush("#F1F1F1");
    private static readonly IBrush LightText = Brush("#151515");
    private static readonly IBrush DarkMuted = Brush("#A8A8A8");
    private static readonly IBrush LightMuted = Brush("#686868");

    public static bool IsLight(Control control) => control.ActualThemeVariant == ThemeVariant.Light;
    public static IBrush Canvas(Control control) => IsLight(control) ? LightCanvas : DarkCanvas;
    public static IBrush Surface(Control control) => IsLight(control) ? LightSurface : DarkSurface;
    public static IBrush Control(Control control) => IsLight(control) ? LightControl : DarkControl;
    public static IBrush Border(Control control) => IsLight(control) ? LightBorder : DarkBorder;
    public static IBrush Grid(Control control) => IsLight(control) ? LightGrid : DarkGrid;
    public static IBrush Text(Control control) => IsLight(control) ? LightText : DarkText;
    public static IBrush Muted(Control control) => IsLight(control) ? LightMuted : DarkMuted;

    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
