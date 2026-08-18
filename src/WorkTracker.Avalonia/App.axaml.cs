using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using WorkTracker.Application;
using WorkTracker.Application.Plugins;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Infrastructure;
using WorkTracker.Infrastructure.Data;
using WorkTracker.UI.Shared;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia;

public partial class App : global::Avalonia.Application
{
	private IHost? _host;
	private IHotkeyService? _hotkeyService;

	// Active theme-mode state used to react to OS theme changes
	private static bool s_followSystemTheme;
	private static string s_singleTheme = ApplicationSettings.DefaultTheme;
	private static string s_lightTheme = ThemeCatalog.DefaultLightTheme;
	private static string s_darkTheme = ThemeCatalog.DefaultDarkTheme;
	private static bool s_systemListenerHooked;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		// Initialize localization early — XAML markup extensions need it before any window is created
		var localization = new LocalizationService();
		LocalizationService.SetInstance(localization);

		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			// Read theme and startMinimized directly from settings.json (fast, no DI needed)
			var (theme, followSystemTheme, lightTheme, darkTheme, startMinimized) = ReadEarlySettings();
			ApplyThemeMode(followSystemTheme, theme, lightTheme, darkTheme);

			if (!startMinimized)
			{
				// Show styled empty window immediately, then load data on background
				var mainWindow = new MainWindow();
				desktop.MainWindow = mainWindow;
				mainWindow.Show();
			}

			Dispatcher.UIThread.Post(async () =>
			{
				try
				{
					await InitializeAsync(desktop, localization, startMinimized);
				}
				catch (Exception ex)
				{
					LogErrorSafe(ex, "Background initialization failed");
				}
			}, DispatcherPriority.Background);
		}

		base.OnFrameworkInitializationCompleted();
	}

	/// <summary>
	/// Bootstraps the application, offering the user a retry when startup failed for a reason
	/// they can fix without restarting — most notably a database on a removable drive that has
	/// not been plugged in yet.
	/// </summary>
	private async Task InitializeAsync(IClassicDesktopStyleApplicationLifetime desktop, LocalizationService localization, bool startMinimized)
	{
		while (true)
		{
			try
			{
				await BootstrapAsync(desktop, localization, startMinimized);
				return;
			}
			catch (Exception ex)
			{
				LogErrorSafe(ex, "Initialization failed");

				// Release the half-built host before retrying — it still holds the Serilog file
				// lock and any SQLite handles that the next attempt needs.
				await DisposeFailedHostAsync();

				if (!await ShowStartupErrorAsync(desktop, localization, ex))
				{
					desktop.Shutdown();
					return;
				}
			}
		}
	}

	private async Task BootstrapAsync(IClassicDesktopStyleApplicationLifetime desktop, LocalizationService localization, bool startMinimized)
	{
		// Build host and bootstrap on a background thread (DI, DB migration, plugins)
		_host = await Task.Run(() =>
		{
			var host = Host.CreateDefaultBuilder()
				.ConfigureAppConfiguration((context, config) =>
				{
					config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
						.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
						.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: false)
						.AddEnvironmentVariables();
				})
				.ConfigureServices((context, services) =>
					ConfigureAppServices(services, context.Configuration, localization))
				.UseSerilog((context, loggerConfiguration) =>
				{
					loggerConfiguration
						.MinimumLevel.Information()
						.MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
						.MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
						.WriteTo.File(WorkTrackerPaths.LogFilePath,
							rollingInterval: RollingInterval.Day,
							retainedFileCountLimit: 14,
							outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");
				})
				.Build();

			return host;
		});

		await _host.StartAsync();

		// DB migration + worklog state (needed before showing data)
		await Infrastructure.DependencyInjection.InitializeDatabaseAsync(_host.Services);
		var worklogStateService = _host.Services.GetRequiredService<IWorklogStateService>();
		await worklogStateService.InitializeAsync();

		// Wire up services to the window on the UI thread
		var viewModel = _host.Services.GetRequiredService<MainViewModel>();
		var trayIconService = _host.Services.GetRequiredService<ITrayIconService>();
		var settingsService = _host.Services.GetRequiredService<ISettingsService>();

		var mainWindow = desktop.MainWindow as MainWindow;
		if (mainWindow == null)
		{
			// startMinimized — window wasn't created yet; show briefly to create HWND for hotkey registration
			mainWindow = new MainWindow { ShowInTaskbar = false, Opacity = 0 };
			desktop.MainWindow = mainWindow;
			try
			{
				mainWindow.Show();
				mainWindow.Hide();
			}
			finally
			{
				mainWindow.ShowInTaskbar = true;
				mainWindow.Opacity = 1;
			}
		}

		mainWindow.Initialize(viewModel, trayIconService, settingsService);

		// Initialize global hotkey (Ctrl+Shift+W) for new work entry dialog
		_hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();
		_hotkeyService.HotkeyPressed += OnHotkeyPressed;
		_hotkeyService.Register();

		// Check for updates (non-blocking, fire-and-forget)
		var updateCheckService = _host.Services.GetService<IUpdateCheckService>();
		if (updateCheckService != null)
		{
			var updateLogger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<App>();
			_ = updateCheckService.CheckForUpdateAsync()
				.SafeFireAndForgetAsync(ex => updateLogger.LogWarning(ex, "Update check failed"));
		}

		// Load plugins in the background — not needed for initial UI
		var pluginLogger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<App>();
		var configuration = _host.Services.GetRequiredService<IConfiguration>();
		var notificationService = _host.Services.GetRequiredService<INotificationService>();
		_ = Task.Run(async () =>
		{
			try
			{
				await Infrastructure.DependencyInjection.InitializePluginsAsync(
					_host.Services, configuration,
					settingsService.Settings.EnabledPlugins,
					settingsService.Settings.PluginConfigurations);

				// Refresh suggestions now that plugins are loaded
				await Dispatcher.UIThread.InvokeAsync(viewModel.NotifyPluginsLoaded);
			}
			catch (Exception pluginEx)
			{
				pluginLogger.LogError(pluginEx, "Plugin initialization failed");
				notificationService.ShowError($"Plugin initialization failed: {pluginEx.Message}");
			}
		});
	}

	/// <summary>
	/// Reports a failed bootstrap. A <see cref="DatabaseUnavailableException"/> is recoverable —
	/// the user can connect the removable drive and try again — so it offers Retry/Close;
	/// any other failure can only be acknowledged. Returns <c>true</c> when the user chose Retry.
	/// </summary>
	private static async Task<bool> ShowStartupErrorAsync(
		IClassicDesktopStyleApplicationLifetime desktop, LocalizationService localization, Exception exception)
	{
		var canRetry = exception is DatabaseUnavailableException;

		var (title, message) = exception is DatabaseUnavailableException databaseException
			? (localization.GetString("DatabaseUnavailableTitle"),
				localization.GetFormattedString("DatabaseUnavailableMessage", databaseException.DatabasePath))
			: (localization.GetString("StartupErrorTitle"),
				localization.GetFormattedString("StartupErrorMessage", exception.Message));

		var errorWindow = new MessageBoxWindow(title, message,
			canRetry ? MessageBoxButtons.RetryClose : MessageBoxButtons.Ok);

		if (desktop.MainWindow is { IsVisible: true } ownerWindow)
		{
			await errorWindow.ShowDialog(ownerWindow);
			return errorWindow.Result && canRetry;
		}

		// Started minimized — the error window is the only window on screen, so closing it for a
		// retry would end the application under the default OnLastWindowClose shutdown mode.
		var previousShutdownMode = desktop.ShutdownMode;
		desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
		try
		{
			// Without an owner the dialog defaults would hide it off-centre and off the taskbar
			errorWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
			errorWindow.ShowInTaskbar = true;
			desktop.MainWindow = errorWindow;

			var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			errorWindow.Closed += (_, _) => closed.TrySetResult();
			errorWindow.Show();
			errorWindow.Activate();
			await closed.Task;

			return errorWindow.Result && canRetry;
		}
		finally
		{
			desktop.MainWindow = null;
			desktop.ShutdownMode = previousShutdownMode;
		}
	}

	/// <summary>
	/// Tears down a host left over from a failed bootstrap so the next attempt starts from a
	/// clean container. Unlike <see cref="ShutdownCleanup"/> it resolves no services — they may
	/// never have been constructed.
	/// </summary>
	private async Task DisposeFailedHostAsync()
	{
		var host = _host;
		if (host == null)
		{
			return;
		}

		try
		{
			await host.StopAsync(TimeSpan.FromSeconds(5));
		}
		catch (Exception ex)
		{
			LogErrorSafe(ex, "Failed to stop host after initialization failure");
		}
		finally
		{
			host.Dispose();
			_host = null;
		}
	}

	/// <summary>
	/// Complete service registration for the Avalonia host. Internal so smoke tests can
	/// build and validate the exact same container the app uses at runtime.
	/// </summary>
	internal static void ConfigureAppServices(IServiceCollection services, IConfiguration configuration, LocalizationService localization)
	{
		services.AddInfrastructure(configuration);
		services.AddUIShared();

		services.AddSingleton(localization);
		services.AddSingleton<ILocalizationService>(localization);

		services.AddSingleton<ISettingsService, SettingsService>();
		services.AddSingleton<IWorklogStateService, WorklogStateService>();

		services.AddSingleton<IDialogService, DialogService>();
		services.AddSingleton<INotificationService, NotificationService>();
		services.AddSingleton<ITrayIconService, TrayIconService>();
		services.AddSingleton<ISystemNotificationService, SystemNotificationService>();
		services.AddSingleton<IAutostartManager, AutostartManager>();
		services.AddSingleton<IHotkeyService, HotkeyService>();

		services.AddSingleton<MainViewModel>();
		services.AddTransient<WorkEntryEditViewModel>();
		services.AddTransient<SubmitWorklogViewModel>();
		services.AddTransient<SettingsViewModel>();
	}

	/// <summary>
	/// Reads theme/follow-system/light/dark + startMinimized directly from settings.json
	/// without DI. This allows showing the correctly themed window before Host.Build() completes.
	/// </summary>
	private static (string theme, bool followSystemTheme, string lightTheme, string darkTheme, bool startMinimized) ReadEarlySettings()
	{
		var defaults = (
			theme: ApplicationSettings.DefaultTheme,
			followSystemTheme: false,
			lightTheme: ThemeCatalog.DefaultLightTheme,
			darkTheme: ThemeCatalog.DefaultDarkTheme,
			startMinimized: false);

		try
		{
			var settingsPath = WorkTrackerPaths.SettingsFilePath;

			if (!File.Exists(settingsPath))
			{
				return defaults;
			}

			using var stream = File.OpenRead(settingsPath);
			using var doc = JsonDocument.Parse(stream);
			var root = doc.RootElement;

			var theme = root.TryGetProperty("Theme", out var t) ? t.GetString() ?? defaults.theme : defaults.theme;
			var followSystem = root.TryGetProperty("FollowSystemTheme", out var f) && f.GetBoolean();
			var lightTheme = root.TryGetProperty("LightTheme", out var lt) ? lt.GetString() ?? defaults.lightTheme : defaults.lightTheme;
			var darkTheme = root.TryGetProperty("DarkTheme", out var dt) ? dt.GetString() ?? defaults.darkTheme : defaults.darkTheme;
			var startMinimized = root.TryGetProperty("StartMinimized", out var s) && s.GetBoolean();

			return (theme, followSystem, lightTheme, darkTheme, startMinimized);
		}
		catch
		{
			return defaults;
		}
	}

	private async void OnHotkeyPressed(object? sender, EventArgs e)
	{
		try
		{
			await Dispatcher.UIThread.InvokeAsync(async () =>
			{
				var dialogService = _host!.Services.GetRequiredService<IDialogService>();
				await dialogService.ShowNewWorkEntryDialogAsync();
			});
		}
		catch (Exception ex)
		{
			LogErrorSafe(ex, "Failed to open work entry dialog");
		}
	}

	/// <summary>
	/// Tears down services and the host after the Avalonia main loop has exited
	/// (called from Program.Main). Runs on the former UI thread with no Dispatcher
	/// pumping, so the async host teardown is bounded by <paramref name="timeout"/> —
	/// a stuck plugin must not keep the process alive and block OS shutdown.
	/// </summary>
	internal void ShutdownCleanup(TimeSpan timeout)
	{
		try
		{
			_hotkeyService?.Unregister();

			if (_host == null)
			{
				return;
			}

			_host.Services.GetRequiredService<MainViewModel>().Dispose();

			var pluginManager = _host.Services.GetRequiredService<IPluginManager>();
			var host = _host;
			var teardown = Task.Run(async () =>
			{
				await pluginManager.DisposeAsync();
				await host.StopAsync();
			});

			if (teardown.Wait(timeout))
			{
				_host.Dispose();
			}
			else
			{
				// Skip Dispose — it could block indefinitely too, and the process is exiting anyway
				LogErrorSafe(new TimeoutException($"Host teardown did not finish within {timeout.TotalSeconds:0} s"),
					"Host teardown timed out");
			}

			_host = null;
		}
		catch (Exception ex)
		{
			LogErrorSafe(ex, "Failed to shut down host");
		}
	}

	private void LogErrorSafe(Exception ex, string message)
	{
		try
		{
			var logger = _host?.Services.GetService<ILogger<App>>();
			if (logger != null)
			{
				logger.LogError(ex, message);
				return;
			}
		}
		catch
		{
			// Logger resolution failed (host disposed/unavailable) — fall through to Console.Error.
		}

		Console.Error.WriteLine($"{message}: {ex}");
	}

	/// <summary>
	/// Applies the current theme based on whether the user wants to follow the OS day/night
	/// setting or stick to a single named theme. Also wires up (or removes) the system listener
	/// so OS changes propagate while in follow-system mode.
	/// </summary>
	public static void ApplyThemeMode(bool followSystemTheme, string singleTheme, string lightTheme, string darkTheme)
	{
		s_followSystemTheme = followSystemTheme;
		s_singleTheme = singleTheme;
		s_lightTheme = lightTheme;
		s_darkTheme = darkTheme;

		EnsureSystemThemeListener();
		SwitchTheme(ResolveEffectiveTheme());
	}

	/// <summary>
	/// Applies a single theme immediately and switches the app out of follow-system mode.
	/// Used by the settings preview when the user changes the single-theme dropdown.
	/// </summary>
	public static void SwitchTheme(string themeName)
	{
		var app = (App)global::Avalonia.Application.Current!;
		var resources = app.Resources as global::Avalonia.Controls.ResourceDictionary;
		if (resources?.MergedDictionaries == null)
		{
			return;
		}

		// Remove the currently loaded theme dictionary, if any.
		// _Defaults.axaml is the contract fallback and stays loaded; only the active theme
		// is swapped out so its keys layer on top of (override) the defaults.
		var existing = resources.MergedDictionaries
			.OfType<ResourceInclude>()
			.FirstOrDefault(r =>
			{
				var src = r.Source?.ToString();
				return src != null
					&& src.Contains("/Themes/")
					&& !src.Contains("_Defaults.axaml");
			});
		if (existing != null)
		{
			resources.MergedDictionaries.Remove(existing);
		}

		// Resolve the URI for the requested theme
		var uri = themeName switch
		{
			"Light" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/OneLightTheme.axaml"),
			"Purple" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/PurpleTheme.axaml"),
			"Midnight" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/MidnightTheme.axaml"),
			"Abyss" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Abyss.axaml"),
			"Cobalt" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Cobalt.axaml"),
			"Coral" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Coral.axaml"),
			"Eclipse" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Eclipse.axaml"),
			"Sandstone" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Sandstone.axaml"),
			"Synthwave" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/Synthwave.axaml"),
			ApplicationSettings.DefaultTheme => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/ModernBlueTheme.axaml"),
			_ => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/OneDarkTheme.axaml")
		};
		resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });

		// Keep Avalonia's built-in FluentTheme variant in sync for native controls
		app.RequestedThemeVariant = ThemeCatalog.IsLight(themeName)
			? global::Avalonia.Styling.ThemeVariant.Light
			: global::Avalonia.Styling.ThemeVariant.Dark;

		ThemeChanged?.Invoke(app, EventArgs.Empty);
	}

	/// <summary>
	/// Subscribes to OS color-mode changes once. The handler re-applies the current theme
	/// mode, which picks light/dark when in follow-system mode and is a no-op otherwise.
	/// </summary>
	private static void EnsureSystemThemeListener()
	{
		if (s_systemListenerHooked)
		{
			return;
		}

		var app = (App?)global::Avalonia.Application.Current;
		var platform = app?.PlatformSettings;
		if (platform == null)
		{
			return;
		}

		platform.ColorValuesChanged += (_, _) =>
		{
			if (!s_followSystemTheme)
			{
				return;
			}

			Dispatcher.UIThread.Post(() => SwitchTheme(ResolveEffectiveTheme()));
		};

		s_systemListenerHooked = true;
	}

	private static string ResolveEffectiveTheme()
	{
		if (!s_followSystemTheme)
		{
			return s_singleTheme;
		}

		var app = (App?)global::Avalonia.Application.Current;
		var systemVariant = app?.PlatformSettings?.GetColorValues().ThemeVariant;
		return systemVariant == global::Avalonia.Platform.PlatformThemeVariant.Light
			? s_lightTheme
			: s_darkTheme;
	}

	public static event EventHandler? ThemeChanged;
}