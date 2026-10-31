using System.Diagnostics;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Tweakr.App.Services;
using Tweakr.App.ViewModels;
using Tweakr.Core;
using Tweakr.Tweaks;

namespace Tweakr.App;

public partial class App : Application
{
    ServiceProvider? _services;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var sc = new ServiceCollection();
        sc.AddSingleton(WindowsVersionInfo.Current);
        sc.AddSingleton<EngineOptions>();
        sc.AddSingleton(_ => new HistoryStore());
        sc.AddSingleton<RestorePointService>();
        sc.AddSingleton<IReadOnlyList<ITweak>>(_ =>
            TweakLoader.Load(BuiltInTweaks.GetJson(), msg => Debug.WriteLine("[Tweakr] " + msg)));
        sc.AddSingleton<TweakEngine>();
        sc.AddSingleton<SettingsStore>();
        sc.AddSingleton<ThemeService>();
        sc.AddSingleton<SystemInfoService>();
        sc.AddSingleton<DialogService>();
        sc.AddSingleton<ToastService>();
        sc.AddSingleton<DiscordPresenceService>();
        sc.AddSingleton<StartupService>();
        sc.AddSingleton<StartupRunner>();
        sc.AddSingleton<StartupViewModel>();
        sc.AddSingleton<UtilitiesViewModel>();
        sc.AddSingleton<DebloatService>();
        sc.AddSingleton<DebloatViewModel>();
        sc.AddSingleton<SettingsViewModel>();
        sc.AddSingleton<MainViewModel>();
        sc.AddSingleton<MainWindow>();
        _services = sc.BuildServiceProvider();

        // Launched by the scheduled task at sign-in: no window, apply the selected tweaks, exit.
        if (e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _ = RunStartupModeAsync();
            return;
        }

        _services.GetRequiredService<MainWindow>().Show();
    }

    async Task RunStartupModeAsync()
    {
        try { await _services!.GetRequiredService<StartupRunner>().RunAsync(); }
        catch { /* nothing to show: we're running headless */ }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
