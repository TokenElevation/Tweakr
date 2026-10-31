using System.Windows;
using System.Windows.Controls;

namespace Tweakr.App.Behaviors;

/// <summary>
/// Responsive column layout: as many columns as fit (each at least MinItemWidth), items share the width evenly.
/// Items go to columns round-robin and stack vertically, so expanding one card only moves the cards below it
/// in the same column; its neighbours never stretch or shift.
/// </summary>
public sealed class FluidWrapPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty =
        DependencyProperty.Register(nameof(MinItemWidth), typeof(double), typeof(FluidWrapPanel),
            new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    (int cols, double itemWidth) Layout(double width)
    {
        int cols = Math.Max(1, (int)(width / MinItemWidth));
        return (cols, width / cols);
    }

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? MinItemWidth : available.Width;
        var (cols, itemW) = Layout(w);
        var heights = new double[cols];
        int i = 0;
        foreach (UIElement child in InternalChildren)
        {
            child.Measure(new Size(itemW, double.PositiveInfinity));
            heights[i % cols] += child.DesiredSize.Height;
            i++;
        }
        return new Size(w, heights.Max());
    }

    protected override Size ArrangeOverride(Size final)
    {
        var (cols, itemW) = Layout(final.Width);
        var heights = new double[cols];
        int i = 0;
        foreach (UIElement child in InternalChildren)
        {
            int c = i % cols;
            child.Arrange(new Rect(c * itemW, heights[c], itemW, child.DesiredSize.Height));
            heights[c] += child.DesiredSize.Height;
            i++;
        }
        return final;
    }
}
