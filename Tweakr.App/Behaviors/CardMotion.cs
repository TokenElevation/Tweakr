using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Tweakr.App.ViewModels;

namespace Tweakr.App.Behaviors;

public static class CardMotion
{
    sealed class State
    {
        public readonly ScaleTransform Scale = new(1, 1);
        public readonly TranslateTransform Move = new();
        public bool Entered;
        public Action<bool>? Handler;
    }

    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(CardMotion), new PropertyMetadata(false, OnChanged));
    static readonly DependencyProperty StateProperty =
        DependencyProperty.RegisterAttached("State", typeof(State), typeof(CardMotion));

    public static bool GetIsEnabled(DependencyObject d) => (bool)d.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject d, bool v) => d.SetValue(IsEnabledProperty, v);

    static void OnChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Border b || !(bool)e.NewValue) return;

        var st = new State();
        b.SetValue(StateProperty, st);
        b.RenderTransformOrigin = new Point(0.5, 0.5);
        b.RenderTransform = new TransformGroup { Children = { st.Scale, st.Move } };
        if (Motion.Enabled) b.Opacity = 0;   // revealed by the entrance tween in Loaded

        b.MouseEnter += (_, _) => ScaleTo(st, 1.02, 0.18);
        b.MouseLeave += (_, _) => ScaleTo(st, 1.0, 0.2);
        b.PreviewMouseLeftButtonDown += (_, _) => ScaleTo(st, 0.98, 0.08);
        b.PreviewMouseLeftButtonUp += (_, _) => ScaleTo(st, b.IsMouseOver ? 1.02 : 1.0, 0.18, Ease.OutBack);
        b.Loaded += OnLoaded;
        b.Unloaded += OnUnloaded;
    }

    static void ScaleTo(State st, double v, double sec, IEasingFunction? ease = null)
    {
        Tween.To(st.Scale, ScaleTransform.ScaleXProperty, v, sec, ease);
        Tween.To(st.Scale, ScaleTransform.ScaleYProperty, v, sec, ease);
    }

    static void OnLoaded(object sender, RoutedEventArgs e)
    {
        var b = (Border)sender;
        var st = (State)b.GetValue(StateProperty);

        if (!st.Entered)
        {
            st.Entered = true;
            if (Motion.Enabled)
            {
                int i = VisualTreeHelper.GetParent(b) is ContentPresenter cp ? Math.Max(0, ItemsControl.GetAlternationIndex(cp)) : 0;
                double delay = Math.Min(i, 14) * 0.04;      // stagger
                st.Move.Y = 14;
                Tween.To(b, UIElement.OpacityProperty, 1, 0.35, Ease.OutCubic, delay);
                Tween.To(st.Move, TranslateTransform.YProperty, 0, 0.5, Ease.OutCubic, delay);
            }
            else b.Opacity = 1;
        }

        if (b.DataContext is TweakCardViewModel vm && st.Handler is null)
        {
            st.Handler = ok => Feedback(b, st, ok);
            vm.Feedback += st.Handler;
        }
    }

    static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        var b = (Border)sender;
        var st = (State)b.GetValue(StateProperty);
        if (b.DataContext is TweakCardViewModel vm && st.Handler is not null) { vm.Feedback -= st.Handler; st.Handler = null; }
    }

    static void Feedback(Border b, State st, bool ok)
    {
        if (!Motion.Enabled) return;
        Flash(b, ok ? Color.FromRgb(0x4C, 0xD9, 0x64) : Color.FromRgb(0xFF, 0x5C, 0x5C));
        if (ok) Tween.Pulse(st.Scale, ScaleTransform.ScaleXProperty, 1.04, 0.35);
        if (ok) Tween.Pulse(st.Scale, ScaleTransform.ScaleYProperty, 1.04, 0.35);
        else Tween.Shake(st.Move);
    }

    static void Flash(Border b, Color c)
    {
        var brush = new SolidColorBrush(c);
        b.BorderBrush = brush;
        var anim = new ColorAnimation(Color.FromArgb(0x22, 255, 255, 255), TimeSpan.FromSeconds(1.0)) { EasingFunction = Ease.OutCubic };
        anim.Completed += (_, _) => b.ClearValue(Border.BorderBrushProperty);   // back to the style's border
        brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
    }
}
