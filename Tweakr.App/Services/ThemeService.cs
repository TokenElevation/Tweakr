using System.Windows;
using System.Windows.Media;

namespace Tweakr.App.Services;

/// <summary>A background color scheme: solid fallback color + a gradient wash drawn over Mica/Acrylic/Solid.</summary>
public sealed class ThemeOption
{
    public ThemeOption(string name, string solid, string tintFrom, string tintTo, string subtle)
    {
        Name = name;
        SubtleText = (Color)ColorConverter.ConvertFromString(subtle);
        Solid = (Color)ColorConverter.ConvertFromString(solid);
        var a = (Color)ColorConverter.ConvertFromString(tintFrom);
        var b = (Color)ColorConverter.ConvertFromString(tintTo);
        TintBrush = Gradient(a, b);
        Preview = Gradient(Blend(Solid, a), Blend(Solid, b));
    }

    public string Name { get; }
    public Color Solid { get; }
    /// <summary>Color for descriptions / secondary text; lighter on the colourful themes for contrast.</summary>
    public Color SubtleText { get; }
    public Brush TintBrush { get; }
    public Brush Preview { get; }

    static Brush Gradient(Color a, Color b)
    {
        var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
        g.Freeze();
        return g;
    }

    static Color Blend(Color bg, Color fg)
    {
        double a = fg.A / 255.0;
        return Color.FromRgb((byte)(bg.R * (1 - a) + fg.R * a), (byte)(bg.G * (1 - a) + fg.G * a), (byte)(bg.B * (1 - a) + fg.B * a));
    }

    public static IReadOnlyList<ThemeOption> All() => new[]
    {
        new ThemeOption("Charcoal", "#FF1C1C20", "#00000000", "#00000000", "#FFB3B3BD"),
        new ThemeOption("Midnight", "#FF0A1022", "#5A1E3A8A", "#330F172A", "#FFC0C9E4"),
        new ThemeOption("Aurora",   "#FF120E1F", "#59A855F7", "#4014B8A6", "#FFD3CCEA"),
        new ThemeOption("Ember",    "#FF1A0E0E", "#55EF4444", "#40F97316", "#FFE6D2CC"),
        new ThemeOption("Forest",   "#FF0C1612", "#5522C55E", "#400F766E", "#FFCBDFD5"),
        new ThemeOption("AMOLED",   "#FF000000", "#CC000000", "#CC000000", "#FFB0B0B8"),
    };
}

/// <summary>Applies live theme changes: backdrop, accent color and background theme (all DynamicResource).</summary>
public sealed class ThemeService
{
    Window? _window;
    BackdropKind _backdrop = BackdropKind.Mica;

    public void Attach(Window window) { _window = window; Backdrop.Apply(window, _backdrop); }

    public void SetBackdrop(BackdropKind kind)
    {
        _backdrop = kind;
        if (_window is not null) Backdrop.Apply(_window, kind);
    }

    public void SetAccent(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var res = Application.Current.Resources;
        res["AccentColor"] = color;
        res["AccentBrush"] = brush;
    }

    public void SetTheme(ThemeOption theme)
    {
        var solid = new SolidColorBrush(theme.Solid);
        solid.Freeze();
        var res = Application.Current.Resources;
        res["WindowSolidBrush"] = solid;      // used by the Solid backdrop / Windows 10
        res["ThemeTintBrush"] = theme.TintBrush;
        var subtle = new SolidColorBrush(theme.SubtleText);
        subtle.Freeze();
        res["SubtleTextBrush"] = subtle;
    }
}
