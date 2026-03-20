using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using MaterialDesignThemes.Wpf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Plugins;
using WorkTracker.Infrastructure;
using WorkTracker.Plugin.Tempo;
using WorkTracker.UI.Shared.Services;
using WorkTracker.WPF.Services;
using WorkTracker.WPF.ViewModels;
using WorkTracker.WPF.Views;

namespace WorkTracker.WPF;

/// <summary>
/// Application entry point with dependency injection setup
/// </summary>
public partial class App : System.Windows.Application
{
	private readonly IHost _host;
	private MainWindow? _mainWindow;
	private IHotkeyService? _hotkeyService;

	public App()
	{
		// Set Language property for WPF framework elements (DatePicker, Calendar, etc.)
		// This ensures DatePickers and Calendars use the current thread's culture
		var currentCulture = CultureInfo.CurrentUICulture;
		FrameworkElement.LanguageProperty.OverrideMetadata(
			typeof(FrameworkElement),
			new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(currentCulture.IetfLanguageTag)));

		// Global exception handlers
		DispatcherUnhandledException += OnDispatcherUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

		// Build host with dependency injection
		_host = Host.CreateDefaultBuilder()
			.ConfigureAppConfiguration((context, config) =>
			{
				// Use AppDomain.CurrentDomain.BaseDirectory to ensure we look for config files
				// in the application directory, not the working directory (important for autostart)
				config.SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
					.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
					.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true)
					.AddEnvironmentVariables();
			})
			.ConfigureServices((context, services) =>
			{
				// Infrastructure services (database, API clients, application services)
				services.AddInfrastructure(context.Configuration);
				// Note: IWorkEntryService and IWorklogSubmissionService are registered in Infrastructure layer

				// WPF-specific services
				services.AddSingleton<IDialogService, DialogService>(); // Stateless dialog service (factory for ViewModels)

				// Shared MessageQueue for notifications
				services.AddSingleton<ISnackbarMessageQueue>(new SnackbarMessageQueue(TimeSpan.FromSeconds(3)));
				services.AddSingleton<INotificationService, NotificationService>();
				services.AddSingleton<ITrayIconService, TrayIconService>();
				services.AddSingleton<ISettingsService, SettingsService>();
				services.AddSingleton<IAutostartManager, AutostartManager>();
				services.AddSingleton<IHotkeyService, HotkeyService>();

				// Application state management
				services.AddSingleton<IWorklogStateService, WorklogStateService>();

				// ViewModels - MainViewModel as Singleton (lives for app lifetime), dialogs as Transient
				services.AddSingleton<MainViewModel>();
				services.AddTransient<WorkEntryEditViewModel>();
				services.AddTransient<SubmitWorklogViewModel>();
				services.AddTransient<SettingsViewModel>();

				// Views - MainWindow as Singleton
				services.AddSingleton<MainWindow>();
			})
			.ConfigureLogging(logging =>
			{
				logging.ClearProviders();
				logging.AddDebug();
				logging.AddConsole();
			})
			.Build();
	}

	private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
	{
		MessageBox.Show(
			$"An unhandled exception occurred:\n\n{e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace}",
			"Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);

		e.Handled = true;
	}

	private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		var exception = e.ExceptionObject as Exception;
		MessageBox.Show(
			$"A fatal error occurred:\n\n{exception?.Message}\n\nStack trace:\n{exception?.StackTrace}",
			"Fatal Error",
			MessageBoxButton.OK,
			MessageBoxImage.Error);
	}

	/// <summary>
	/// Application startup - initialize database and show main window
	/// </summary>
	protected override async void OnStartup(StartupEventArgs e)
	{
		try
		{
			while (_host is null)
			{
				await Task.Delay(100);
			}

			await _host.StartAsync();

			// Initialize database
			await DependencyInjection.InitializeDatabaseAsync(_host.Services);

			// Load embedded plugins (presentation layer responsibility)
			var pluginManager = _host.Services.GetRequiredService<PluginManager>();
			pluginManager.LoadEmbeddedPlugin<TempoWorklogPlugin>();

			// Load settings to get enabled plugins and configurations
			var settingsService = _host.Services.GetRequiredService<ISettingsService>();
			var enabledPlugins = settingsService.Settings.EnabledPlugins;
			var pluginConfigurations = settingsService.Settings.PluginConfigurations;

			// Initialize plugins (loads external plugins + initializes all with configuration)
			var configuration = _host.Services.GetRequiredService<IConfiguration>();
			await DependencyInjection.InitializePluginsAsync(_host.Services, configuration, enabledPlugins, pluginConfigurations);

			// Initialize application state service
			var worklogStateService = _host.Services.GetRequiredService<IWorklogStateService>();
			await worklogStateService.InitializeAsync();

			// Get singleton MainWindow (no scope needed with Factory pattern)
			_mainWindow = _host.Services.GetRequiredService<MainWindow>();
			_mainWindow.Show();

			// Initialize global hotkey (Ctrl+Alt+W) for new work entry dialog
			_hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();
			_hotkeyService.HotkeyPressed += OnHotkeyPressed;
			_hotkeyService.Register();

			base.OnStartup(e);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to start application:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace}",
				"Startup Error",
				MessageBoxButton.OK,
				MessageBoxImage.Error);

			Shutdown(1);
		}
	}

	/// <summary>
	/// Handle global hotkey press (Ctrl+Alt+W) to open new work entry dialog
	/// </summary>
	private async void OnHotkeyPressed(object? sender, EventArgs e)
	{
		try
		{
			// Create a new scope for this dialog operation
			using var scope = _host.Services.CreateScope();
			var viewModel = scope.ServiceProvider.GetRequiredService<WorkEntryEditViewModel>();
			await viewModel.InitializeAsync(null);

			// Create dialog without owner (standalone window)
			var dialog = new WorkEntryEditDialog
			{
				DataContext = viewModel,
				Owner = null,
				WindowStartupLocation = WindowStartupLocation.CenterScreen,
				Topmost = true
			};

			dialog.ShowDialog();
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to open work entry dialog: {ex.Message}",
				"Error",
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
	}

	/// <summary>
	/// Application shutdown - cleanup
	/// </summary>
	protected override async void OnExit(ExitEventArgs e)
	{
		// Unregister hotkey
		_hotkeyService?.Unregister();

		// Dispose plugin manager asynchronously to avoid deadlocks
		var pluginManager = _host.Services.GetRequiredService<PluginManager>();
		await pluginManager.DisposeAsync();

		using (_host)
		{
			await _host.StopAsync();
		}

		base.OnExit(e);
	}
}