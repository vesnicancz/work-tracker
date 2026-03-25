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

				// Localization — single instance shared by DI and XAML markup extensions.
				// Must be created before any XAML is loaded.
				var localization = new LocalizationService();
				LocalizationService.SetInstance(localization);
				services.AddSingleton(localization);
				services.AddSingleton<ILocalizationService>(localization);

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
			$"An unhandled exception occurred:\n\n{e.Exception.Message}\n\nStack trace:\n{e.Exception.StackTrace ?? "(not available)"}",
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

			await AppBootstrapper.InitializeAsync(
				_host.Services,
				DependencyInjection.InitializeDatabaseAsync,
				DependencyInjection.InitializePluginsAsync);

			// Get singleton MainWindow (no scope needed with Factory pattern)
			_mainWindow = _host.Services.GetRequiredService<MainWindow>();
			_mainWindow.Show();

			// Initialize global hotkey (Ctrl+Shift+W) for new work entry dialog
			_hotkeyService = _host.Services.GetRequiredService<IHotkeyService>();
			_hotkeyService.HotkeyPressed += OnHotkeyPressed;
			_hotkeyService.Register();

			base.OnStartup(e);
		}
		catch (Exception ex)
		{
			MessageBox.Show(
				$"Failed to start application:\n\n{ex.Message}\n\nStack trace:\n{ex.StackTrace ?? "(not available)"}",
				"Startup Error",
				MessageBoxButton.OK,
				MessageBoxImage.Error);

			Shutdown(1);
		}
	}

	/// <summary>
	/// Handle global hotkey press (Ctrl+Shift+W) to open new work entry dialog
	/// </summary>
	private void OnHotkeyPressed(object? sender, EventArgs e)
	{
		try
		{
			// Create a new scope for this dialog operation
			using var scope = _host.Services.CreateScope();
			var viewModel = scope.ServiceProvider.GetRequiredService<WorkEntryEditViewModel>();
			viewModel.InitializeForNew();

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

		// Dispose MainViewModel (stops timer, unsubscribes events)
		var mainViewModel = _host.Services.GetRequiredService<MainViewModel>();
		mainViewModel.Dispose();

		// Dispose plugin manager asynchronously to avoid deadlocks
		var pluginManager = _host.Services.GetRequiredService<IPluginManager>();
		await pluginManager.DisposeAsync();

		using (_host)
		{
			await _host.StopAsync();
		}

		base.OnExit(e);
	}
}