using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Infrastructure;
using WorkTracker.Plugin.Tempo;
using WorkTracker.UI.Shared.Services;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;

namespace WorkTracker.Avalonia;

public partial class App : global::Avalonia.Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Build host synchronously — no async here
        _host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .AddEnvironmentVariables();
            })
            .ConfigureServices((context, services) =>
            {
                services.AddInfrastructure(context.Configuration);

                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<IWorklogStateService, WorklogStateService>();

                services.AddSingleton<IDialogService, DialogService>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<ITrayIconService, TrayIconService>();
                services.AddSingleton<IAutostartManager, AutostartManager>();

                services.AddSingleton<MainViewModel>();
                services.AddTransient<WorkEntryEditViewModel>();
                services.AddTransient<SubmitWorklogViewModel>();
                services.AddTransient<SettingsViewModel>();

                services.AddSingleton<MainWindow>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
            })
            .Build();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Read settings synchronously to check StartMinimized before showing the window.
            // SettingsService constructor is sync (reads JSON file).
            var earlySettings = _host.Services.GetRequiredService<ISettingsService>();
            var startMinimized = earlySettings.Settings.StartMinimized;

            // Apply saved theme early to avoid flash of wrong theme
            SwitchTheme(earlySettings.Settings.Theme ?? "Dark");

            var mainWindow = new MainWindow();

            if (startMinimized)
            {
                // Prevent visible flash: start hidden with zero opacity
                mainWindow.Opacity = 0;
                mainWindow.ShowInTaskbar = false;
                mainWindow.WindowState = WindowState.Minimized;
            }

            desktop.MainWindow = mainWindow;
            desktop.ShutdownRequested += OnShutdownRequested;

            // Kick off async initialization after the UI is running
            Dispatcher.UIThread.Post(() => _ = InitializeAsync(mainWindow, startMinimized), DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAsync(MainWindow mainWindow, bool startMinimized)
    {
        try
        {
            await _host!.StartAsync();
            await DependencyInjection.InitializeDatabaseAsync(_host.Services);

            // Load plugins
            var pluginManager = _host.Services.GetRequiredService<PluginManager>();
            pluginManager.LoadEmbeddedPlugin<TempoWorklogPlugin>();

            var settingsService = _host.Services.GetRequiredService<ISettingsService>();
            var configuration = _host.Services.GetRequiredService<IConfiguration>();
            await DependencyInjection.InitializePluginsAsync(
                _host.Services, configuration,
                settingsService.Settings.EnabledPlugins,
                settingsService.Settings.PluginConfigurations);

            // Initialize state
            var worklogStateService = _host.Services.GetRequiredService<IWorklogStateService>();
            await worklogStateService.InitializeAsync();

            // Wire up services to the window
            var viewModel = _host.Services.GetRequiredService<MainViewModel>();
            var trayIconService = _host.Services.GetRequiredService<ITrayIconService>();

            mainWindow.Initialize(viewModel, trayIconService, settingsService);

            // If started minimized, restore opacity so tray show works later
            if (startMinimized)
            {
                mainWindow.Opacity = 1;
                mainWindow.ShowInTaskbar = true;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Initialization failed: {ex}");
            Console.Error.WriteLine($"Initialization failed: {ex}");
        }
    }

    private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        if (_host != null)
        {
            var pluginManager = _host.Services.GetRequiredService<PluginManager>();
            await pluginManager.DisposeAsync();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    public static IServiceProvider? Services => ((App)global::Avalonia.Application.Current!)._host?.Services;

    /// <summary>
    /// Switches the active theme at runtime by swapping the top-level MergedDictionary entry
    /// and aligning Avalonia's built-in FluentTheme variant for native controls.
    /// </summary>
    public static void SwitchTheme(string themeName)
    {
        var app = (App)global::Avalonia.Application.Current!;
        var resources = app.Resources as global::Avalonia.Controls.ResourceDictionary;
        if (resources?.MergedDictionaries == null)
        {
            return;
        }

        // Remove the currently loaded theme dictionary, if any
        var existing = resources.MergedDictionaries
            .OfType<ResourceInclude>()
            .FirstOrDefault(r => r.Source?.ToString().Contains("/Themes/") == true);
        if (existing != null)
        {
            resources.MergedDictionaries.Remove(existing);
        }

        // Resolve the URI for the requested theme
        var uri = themeName switch
        {
            "Light" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/OneLightTheme.axaml"),
            "Purple" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/PurpleTheme.axaml"),
            _ => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/OneDarkTheme.axaml")
        };
        resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });

        // Keep Avalonia's built-in FluentTheme variant in sync for native controls
        app.RequestedThemeVariant = themeName == "Light"
            ? global::Avalonia.Styling.ThemeVariant.Light
            : global::Avalonia.Styling.ThemeVariant.Dark;
    }
}
