using System.Windows;
using System.Windows.Media;

namespace Tweakr.App.Behaviors;

/// <summary>Slides an element in from the side (and fades it) when it first loads. Usage: b:Entrance.OffsetX="56"</summary>
public static class Entrance
{
    public static readonly DependencyProperty OffsetXProperty =
        DependencyProperty.RegisterAttached("OffsetX", typeof(double), typeof(Entrance), new PropertyMetadata(0.0, OnChanged));

    public static double GetOffsetX(DependencyObject d) => (double)d.GetValue(OffsetXProperty);
    public static void SetOffsetX(DependencyObject d, double v) => d.SetValue(OffsetXProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe || (double)e.NewValue == 0) return;

        var t = new TranslateTransform((double)e.NewValue, 0);
        fe.RenderTransform = t;
        if (Motion.Enabled) fe.Opacity = 0;

        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            fe.Loaded -= handler;
            Tween.To(fe, UIElement.OpacityProperty, 1, 0.3);
            Tween.To(t, TranslateTransform.XProperty, 0, 0.45, Ease.OutCubic);
        };
        fe.Loaded += handler;
    }
}
