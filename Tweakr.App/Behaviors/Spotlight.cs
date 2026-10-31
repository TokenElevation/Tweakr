using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Tweakr.App.Behaviors;

/// <summary>Radial highlight that follows the mouse over a Border. Usage: b:Spotlight.IsEnabled="True"</summary>
public static class Spotlight
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(Spotlight), new PropertyMetadata(false, OnChanged));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b) return;
        b.MouseMove -= Move; b.MouseLeave -= Leave;
        if ((bool)e.NewValue) { b.MouseMove += Move; b.MouseLeave += Leave; }
    }

    static void Move(object sender, MouseEventArgs e)
    {
        var b = (Border)sender;
        if (b.ActualWidth < 1 || b.ActualHeight < 1) return;
        var p = e.GetPosition(b);
        var c = new Point(p.X / b.ActualWidth, p.Y / b.ActualHeight);
        b.Background = new RadialGradientBrush
        {
            Center = c, GradientOrigin = c, RadiusX = 0.9, RadiusY = 1.4,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x30, 255, 255, 255), 0),
                new GradientStop(Color.FromArgb(0x14, 255, 255, 255), 1)
            }
        };
    }

    // Clearing the local value falls back to the style's Background.
    static void Leave(object sender, MouseEventArgs e) => ((Border)sender).ClearValue(Border.BackgroundProperty);
}
