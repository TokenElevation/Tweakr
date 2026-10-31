using System.Windows;
using System.Windows.Media.Animation;

namespace Tweakr.App.Behaviors;

/// <summary>
/// Animates Height + Opacity when IsOpen changes. The element should start Visibility="Collapsed".
/// Only the element itself grows, so (with VerticalAlignment=Top on cards) neighbours in the same row stay put.
/// </summary>
public static class Reveal
{
    public static readonly DependencyProperty IsOpenProperty =
        DependencyProperty.RegisterAttached("IsOpen", typeof(bool), typeof(Reveal), new PropertyMetadata(false, OnChanged));

    public static bool GetIsOpen(DependencyObject d) => (bool)d.GetValue(IsOpenProperty);
    public static void SetIsOpen(DependencyObject d, bool v) => d.SetValue(IsOpenProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement fe) return;
        bool open = (bool)e.NewValue;
        fe.ClipToBounds = true;
        fe.BeginAnimation(FrameworkElement.HeightProperty, null);
        fe.BeginAnimation(UIElement.OpacityProperty, null);

        if (!Motion.Enabled || !fe.IsLoaded)
        {
            fe.ClearValue(FrameworkElement.HeightProperty);
            fe.Opacity = 1;
            fe.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }

        if (open)
        {
            fe.ClearValue(FrameworkElement.HeightProperty);
            fe.Visibility = Visibility.Visible;
            double w = (fe.Parent as FrameworkElement)?.ActualWidth ?? 300;
            fe.Measure(new Size(w, double.PositiveInfinity));
            double target = fe.DesiredSize.Height;

            var h = new DoubleAnimation(0, target, TimeSpan.FromSeconds(0.32)) { EasingFunction = Ease.OutCubic };
            h.Completed += (_, _) => { fe.BeginAnimation(FrameworkElement.HeightProperty, null); fe.ClearValue(FrameworkElement.HeightProperty); };
            fe.BeginAnimation(FrameworkElement.HeightProperty, h);
            fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.28)));
        }
        else
        {
            double from = fe.ActualHeight;
            var h = new DoubleAnimation(from, 0, TimeSpan.FromSeconds(0.24)) { EasingFunction = Ease.InOutCubic };
            h.Completed += (_, _) =>
            {
                fe.BeginAnimation(FrameworkElement.HeightProperty, null);
                fe.ClearValue(FrameworkElement.HeightProperty);
                fe.Visibility = Visibility.Collapsed;
                fe.Opacity = 1;
            };
            fe.BeginAnimation(FrameworkElement.HeightProperty, h);
            fe.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromSeconds(0.18)));
        }
    }
}
