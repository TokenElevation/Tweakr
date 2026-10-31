using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Tweakr.App.Behaviors;
using Tweakr.App.Services;
using Tweakr.App.ViewModels;

namespace Tweakr.App;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;
    bool _skipCategoryTransition;
    bool _closing, _closeApproved;
    WindowState _lastState = WindowState.Normal;

    public MainWindow(MainViewModel vm, ThemeService theme)
    {
        InitializeComponent();
        DataContext = _vm = vm;

        SourceInitialized += (_, _) =>
        {
            NativeWindow.EnableSystemAnimations(this);       // Windows' own minimize / restore / maximize / open / close animations
            theme.Attach(this);                             // Mica / Acrylic / Solid
        };
        Loaded += async (_, _) => { PlayOpen(); await vm.InitializeAsync(); };
        Closing += OnClosing;
        StateChanged += (_, _) =>
        {
            if (_lastState == WindowState.Minimized && WindowState != WindowState.Minimized) PlayRestore();
            _lastState = WindowState;
            bool max = WindowState == WindowState.Maximized;
            MaxButton.Content = max ? "\uE923" : "\uE922";
            // A borderless window with WindowChrome overshoots the screen by the invisible resize border when maximized;
            // pulling the content in by that amount keeps the title bar, sidebar and scrollbar fully on screen.
            Root.Margin = max ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        };

        vm.PropertyChanged += OnViewModelChanged;
        vm.Dialog.PropertyChanged += OnDialogChanged;

        // Dialog keyboard: Enter = primary, Esc = cancel (and keep keys away from the page underneath)
        PreviewKeyDown += (_, e) =>
        {
            if (!_vm.Dialog.IsOpen) return;
            if (e.Key == Key.Escape) { _vm.Dialog.Cancel(); e.Handled = true; }
            else if (e.Key == Key.Enter) { _vm.Dialog.Confirm(); e.Handled = true; }
        };
    }

    // ---------- window open / close ----------
    void PlayOpen()
    {
        // Root starts at Opacity=0 / Scale=0.96 in XAML. With Windows animations off, never leave it invisible.
        if (!SystemParameters.ClientAreaAnimation)
        {
            Root.Opacity = 1;
            RootScale.ScaleX = RootScale.ScaleY = 1;
            return;
        }
        Tween.From(Root, UIElement.OpacityProperty, 0, 1, 0.4);
        Tween.From(RootScale, ScaleTransform.ScaleXProperty, 0.96, 1, 0.5, Ease.OutCubic);
        Tween.From(RootScale, ScaleTransform.ScaleYProperty, 0.96, 1, 0.5, Ease.OutCubic);
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_closeApproved) return;
        e.Cancel = true;                       // play the exit animation first, then really close
        if (_closing) return;
        _closing = true;

        Tween.To(Root, UIElement.OpacityProperty, 0, 0.18, Ease.InCubic);
        Tween.To(RootScale, ScaleTransform.ScaleXProperty, 0.97, 0.18, Ease.InCubic);
        Tween.To(RootScale, ScaleTransform.ScaleYProperty, 0.97, 0.18, Ease.InCubic, done: () =>
        {
            _closeApproved = true;
            Dispatcher.BeginInvoke(new Action(Close));   // not inside the Closing handler
        });
    }

    // a small content animation on top of the system one when coming back from the taskbar
    void PlayRestore()
    {
        Tween.From(Root, UIElement.OpacityProperty, 0.6, 1, 0.22);
        Tween.From(RootScale, ScaleTransform.ScaleXProperty, 0.97, 1, 0.28, Ease.OutCubic);
        Tween.From(RootScale, ScaleTransform.ScaleYProperty, 0.97, 1, 0.28, Ease.OutCubic);
    }

    // ---------- sidebar ----------
    // Clicking the already-selected category doesn't change SelectedCategory, so nothing would happen when
    // another page (Settings, Utilities...) is open. Bring the Tweaks page back explicitly.
    void Category_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.Page == AppPage.Tweaks) return;
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is ListBoxItem) { _vm.OpenTweaksCommand.Execute(null); return; }
            if (d is ListBox) return;
        }
    }

    // ---------- page transitions ----------
    void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Page):
                if (_vm.Page == AppPage.Dashboard) Transition(DashboardHost, DashboardT);
                else if (_vm.Page == AppPage.Utilities) Transition(UtilitiesHost, UtilitiesT);
                else if (_vm.Page == AppPage.Debloat) Transition(DebloatHost, DebloatT);
                else if (_vm.Page == AppPage.Startup) Transition(StartupHost, StartupT);
                else if (_vm.Page == AppPage.Settings) Transition(SettingsHost, SettingsT);
                else { _skipCategoryTransition = true; Transition(CardsHost, CardsT); }
                break;

            case nameof(MainViewModel.SelectedCategory):
                if (_skipCategoryTransition) { _skipCategoryTransition = false; break; }
                if (_vm.SelectedCategory is not null && _vm.Page == AppPage.Tweaks) Transition(CardsHost, CardsT);
                break;
        }
    }

    // Big pages (All, Debloat+) are built/measured the moment they become visible. Starting the tween at that exact
    // moment makes it freeze and then jump. Hide the page, let layout finish, then play the animation.
    // - old held animations are removed first, otherwise they override the local Opacity=0 and the page flashes in
    // - several triggers in the same tick (Page + SelectedCategory) are coalesced into one transition per host
    readonly HashSet<UIElement> _pendingTransitions = new();

    void Transition(UIElement host, TranslateTransform t)
    {
        if (!_pendingTransitions.Add(host)) return;

        host.BeginAnimation(UIElement.OpacityProperty, null);
        t.BeginAnimation(TranslateTransform.YProperty, null);
        host.Opacity = 0;

        host.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _pendingTransitions.Remove(host);
            host.Opacity = 1;   // final value; the tween below starts from 0
            Tween.From(host, UIElement.OpacityProperty, 0, 1, 0.3);
            Tween.From(t, TranslateTransform.YProperty, 16, 0, 0.4, Ease.OutCubic);
        }));
    }

    // ---------- dialog open / close animation ----------
    void OnDialogChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DialogService.IsOpen)) return;
        var d = _vm.Dialog;

        if (d.IsOpen)
        {
            DialogLayer.Visibility = Visibility.Visible;
            Tween.From(Scrim, UIElement.OpacityProperty, 0, 1, 0.2);
            Tween.From(DialogCard, UIElement.OpacityProperty, 0, 1, 0.22);
            Tween.From(DialogScale, ScaleTransform.ScaleXProperty, 0.92, 1, 0.32, Ease.OutBack);
            Tween.From(DialogScale, ScaleTransform.ScaleYProperty, 0.92, 1, 0.32, Ease.OutBack);
            DialogCard.Focus();
        }
        else
        {
            Tween.To(Scrim, UIElement.OpacityProperty, 0, 0.16);
            Tween.To(DialogCard, UIElement.OpacityProperty, 0, 0.14, Ease.InCubic,
                done: () => { if (!d.IsOpen) DialogLayer.Visibility = Visibility.Collapsed; });   // may have reopened meanwhile
        }
    }

    void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object sender, RoutedEventArgs e) => Close();
}