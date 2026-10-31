using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Tweakr.App.ViewModels;

/// <summary>One dashboard card: icon, title, and two label/value rows.</summary>
public sealed partial class InfoCardViewModel : ObservableObject
{
    public InfoCardViewModel(string title, string subtitle, string iconPath, Color tint, string label1, string label2)
    {
        Title = title; Subtitle = subtitle; Label1 = label1; Label2 = label2;

        var geo = Geometry.Parse("F1 " + iconPath);   // F1 = non-zero fill, like the source SVG
        geo.Freeze();
        Icon = geo;

        var b = new SolidColorBrush(tint); b.Freeze(); Tint = b;
        var f = new SolidColorBrush(Color.FromArgb(0x26, tint.R, tint.G, tint.B)); f.Freeze(); TintFaint = f;
    }

    public string Title { get; }
    public string Subtitle { get; }
    public string Label1 { get; }
    public string Label2 { get; }
    public Geometry Icon { get; }
    public Brush Tint { get; }
    public Brush TintFaint { get; }

    [ObservableProperty] private string _value1 = "…";
    [ObservableProperty] private string _value2 = "…";
}
