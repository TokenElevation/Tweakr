using System.Windows;
using System.Windows.Media.Animation;

namespace Tweakr.App.Behaviors;

/// <summary>Global motion switch. Respects both the in-app setting and Windows' "Animation effects".</summary>
public static class Motion
{
    public static bool UserEnabled { get; set; } = true;
    public static bool Enabled => UserEnabled && SystemParameters.ClientAreaAnimation;
}

public static class Ease
{
    static T F<T>(T f) where T : Freezable { f.Freeze(); return f; }
    public static readonly IEasingFunction OutCubic = F(new CubicEase { EasingMode = EasingMode.EaseOut });
    public static readonly IEasingFunction InCubic = F(new CubicEase { EasingMode = EasingMode.EaseIn });
    public static readonly IEasingFunction InOutCubic = F(new CubicEase { EasingMode = EasingMode.EaseInOut });
    public static readonly IEasingFunction OutBack = F(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 });
    public static readonly IEasingFunction OutExpo = F(new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 });
}

/// <summary>
/// Tiny tween API over WPF animations.
///   Tween.To(card, UIElement.OpacityProperty, 1, 0.35, Ease.OutCubic, delay: i * 0.04);
/// Works on UIElements and on Animatables such as Scale/TranslateTransform. Falls back to an instant set when motion is off.
/// </summary>
public static class Tween
{
    public static void To(IAnimatable target, DependencyProperty dp, double to, double seconds,
        IEasingFunction? ease = null, double delay = 0, Action? done = null)
    {
        if (!Motion.Enabled || seconds <= 0)
        {
            target.BeginAnimation(dp, null);
            ((DependencyObject)target).SetValue(dp, to);
            done?.Invoke();
            return;
        }
        var a = new DoubleAnimation(to, TimeSpan.FromSeconds(seconds))
        {
            EasingFunction = ease ?? Ease.OutCubic,
            BeginTime = TimeSpan.FromSeconds(delay)   // never null: null means 'never starts'
        };
        if (done is not null) a.Completed += (_, _) => done();
        target.BeginAnimation(dp, a);
    }

    /// <summary>Explicit start value (no delay support: before a delayed animation begins, WPF shows the base value).</summary>
    public static void From(IAnimatable target, DependencyProperty dp, double from, double to, double seconds, IEasingFunction? ease = null)
    {
        if (!Motion.Enabled)
        {
            target.BeginAnimation(dp, null);
            ((DependencyObject)target).SetValue(dp, to);
            return;
        }
        target.BeginAnimation(dp, new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds)) { EasingFunction = ease ?? Ease.OutCubic });
    }

    /// <summary>Go to <paramref name="peak"/> and come back, then release the property.</summary>
    public static void Pulse(IAnimatable target, DependencyProperty dp, double peak, double seconds)
    {
        if (!Motion.Enabled) return;
        target.BeginAnimation(dp, new DoubleAnimation(peak, TimeSpan.FromSeconds(seconds / 2))
        {
            AutoReverse = true, EasingFunction = Ease.OutCubic, FillBehavior = FillBehavior.Stop
        });
    }

    public static void Shake(IAnimatable translateX, double amplitude = 8, double seconds = 0.4)
    {
        if (!Motion.Enabled) return;
        double[] f = { 0.0, -1, 1, -0.75, 0.75, -0.35, 0.35, 0 };
        var kf = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(seconds), FillBehavior = FillBehavior.Stop };
        for (int i = 0; i < f.Length; i++)
            kf.KeyFrames.Add(new LinearDoubleKeyFrame(f[i] * amplitude, KeyTime.FromPercent((i + 1) / (double)f.Length)));
        translateX.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty, kf);
    }
}
