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
using WorkTracker.Application.Plugins;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Infrastructure;
using WorkTracker.UI.Shared;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia;

public partial class App : global::Avalonia.Application
{
	private IHost? _host;
	private IHotkeyService? _hotkeyService;

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
			desktop.ShutdownRequested += OnShutdownRequested;

			// Read theme and startMinimized directly from settings.json (fast, no DI needed)
			var (theme, startMinimized) = ReadEarlySettings();
			SwitchTheme(theme);

			if (startMinimized)
			{
				// Don't show window, just start background init
				Dispatcher.UIThread.Post(() => _ = InitializeAsync(desktop, localization, startMinimized: true), DispatcherPriority.Background);
			}
			else
			{
				// Show styled empty window immediately, then load data on background
				var mainWindow = new MainWindow();
				desktop.MainWindow = mainWindow;
				mainWindow.Show();

				Dispatcher.UIThread.Post(() => _ = InitializeAsync(desktop, localization, startMinimized: false), DispatcherPriority.Background);
			}
		}

		base.OnFrameworkInitializationCompleted();
	}

	private async Task InitializeAsync(IClassicDesktopStyleApplicationLifetime desktop, LocalizationService localization, bool startMinimized)
	{
		try
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
					{
						services.AddInfrastructure(context.Configuration);
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
					})
					.ConfigureLogging(logging =>
					{
						logging.ClearProviders();
						logging.AddDebug();
						logging.AddConsole();
					})
					.Build();

				return host;
			});

			await _host.StartAsync();

			// DB migration + worklog state (needed before showing data)
			await DependencyInjection.InitializeDatabaseAsync(_host.Services);
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

			mainWindow.Initialize(viewModel, trayIconService, settingsService, worklogStateService);

			// Initialize global hotkey (Ctrl+Shift+W) for new work entry dialog
			_hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();
			_hotkeyService.HotkeyPressed += OnHotkeyPressed;
			_hotkeyService.Register();

			// Load plugins in the background — not needed for initial UI
			var pluginLogger = _host.Services.GetRequiredService<ILoggerFactory>().CreateLogger<App>();
			var configuration = _host.Services.GetRequiredService<IConfiguration>();
			_ = Task.Run(async () =>
			{
				try
				{
					await DependencyInjection.InitializePluginsAsync(
						_host.Services, configuration,
						settingsService.Settings.EnabledPlugins,
						settingsService.Settings.PluginConfigurations);
				}
				catch (Exception pluginEx)
				{
					pluginLogger.LogError(pluginEx, "Plugin initialization failed");
				}
			});
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Initialization failed: {ex}");
			Console.Error.WriteLine($"Initialization failed: {ex}");

			// Show error dialog
			var errorWindow = new MessageBoxWindow("Initialization Error",
				$"Application failed to initialize:\n{ex.Message}", false);

			if (desktop.MainWindow is Window ownerWindow)
			{
				await errorWindow.ShowDialog(ownerWindow);
			}
			else
			{
				desktop.MainWindow = errorWindow;
				errorWindow.Show();
			}
		}
	}

	/// <summary>
	/// Reads theme and startMinimized directly from settings.json without DI.
	/// This allows showing the correctly themed window before Host.Build() completes.
	/// </summary>
	private static (string theme, bool startMinimized) ReadEarlySettings()
	{
		try
		{
			var appDataFolder = "WorkTracker";
			var env = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
				?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
			if (!string.IsNullOrEmpty(env) && env != "Production")
			{
				appDataFolder += $"_{env}";
			}

			var settingsPath = Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				appDataFolder, "settings.json");

			if (!File.Exists(settingsPath))
			{
				return (ApplicationSettings.DefaultTheme, false);
			}

			using var stream = File.OpenRead(settingsPath);
			using var doc = JsonDocument.Parse(stream);
			var root = doc.RootElement;

			var theme = root.TryGetProperty("Theme", out var t) ? t.GetString() ?? ApplicationSettings.DefaultTheme : ApplicationSettings.DefaultTheme;
			var startMinimized = root.TryGetProperty("StartMinimized", out var s) && s.GetBoolean();

			return (theme, startMinimized);
		}
		catch
		{
			return (ApplicationSettings.DefaultTheme, false);
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
			System.Diagnostics.Debug.WriteLine($"Failed to open work entry dialog: {ex}");
		}
	}

	private async void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
	{
		try
		{
			if (_host != null)
			{
				_hotkeyService?.Unregister();
				var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
				mainViewModel.Dispose();
				var pluginManager = _host.Services.GetRequiredService<IPluginManager>();
				await pluginManager.DisposeAsync();
				await _host.StopAsync();
				_host.Dispose();
			}
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"Failed to shut down host: {ex}");
		}
	}

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
			"Midnight" => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/MidnightTheme.axaml"),
			ApplicationSettings.DefaultTheme => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/ModernBlueTheme.axaml"),
			_ => new Uri("avares://WorkTracker.Avalonia/Resources/Themes/OneDarkTheme.axaml")
		};
		resources.MergedDictionaries.Add(new ResourceInclude(uri) { Source = uri });

		// Keep Avalonia's built-in FluentTheme variant in sync for native controls
		app.RequestedThemeVariant = (themeName is "Light" or ApplicationSettings.DefaultTheme)
			? global::Avalonia.Styling.ThemeVariant.Light
			: global::Avalonia.Styling.ThemeVariant.Dark;

		ThemeChanged?.Invoke(null, EventArgs.Empty);
	}

	public static event EventHandler? ThemeChanged;
}