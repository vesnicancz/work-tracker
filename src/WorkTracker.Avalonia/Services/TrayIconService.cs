using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.Logging;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Services;

/// <summary>
/// Tray icon service using Avalonia's built-in TrayIcon with NativeMenu.
/// </summary>
public sealed class TrayIconService : ITrayIconService, IDisposable
{
	private readonly IDialogService _dialogService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly ISettingsService _settingsService;
	private readonly ILogger<TrayIconService> _logger;
	private readonly ILocalizationService _localizationService;
	private TrayIcon? _trayIcon;
	private NativeMenu? _menu;
	private bool _isInitialized;
	private readonly List<NativeMenuItem> _favoriteMenuItems = new();
	private readonly CancellationTokenSource _cts = new();
	private NativeMenuItemSeparator? _favoritesSeparator;

	public TrayIconService(
		IDialogService dialogService,
		IWorklogStateService worklogStateService,
		ISettingsService settingsService,
		ILocalizationService localizationService,
		ILogger<TrayIconService> logger)
	{
		_dialogService = dialogService;
		_worklogStateService = worklogStateService;
		_settingsService = settingsService;
		_localizationService = localizationService;
		_logger = logger;
	}

	public void Initialize()
	{
		if (_isInitialized)
		{
			return;
		}

		_menu = new NativeMenu();

		// Show window
		var showItem = new NativeMenuItem($"❐ {_localizationService["TrayShow"]}");
		showItem.Click += (_, _) => ShowMainWindow();
		_menu.Items.Add(showItem);

		// New work entry
		var newEntryItem = new NativeMenuItem($"➕ {_localizationService["TrayNewWorkEntry"]}");
		newEntryItem.Click += async (_, _) => await OpenNewWorkEntryAsync();
		_menu.Items.Add(newEntryItem);

		// Favorites will be inserted here
		RefreshFavoritesMenu();

		// Separator before exit
		_menu.Items.Add(new NativeMenuItemSeparator());

		// Exit
		var exitItem = new NativeMenuItem($"⏻ {_localizationService["TrayExit"]}");
		exitItem.Click += (_, _) =>
		{
			if (global::Avalonia.Application.Current?.ApplicationLifetime
				is IClassicDesktopStyleApplicationLifetime desktop)
			{
				desktop.Shutdown();
			}
		};
		_menu.Items.Add(exitItem);

		_trayIcon = new TrayIcon
		{
			ToolTipText = _localizationService["TrayTooltip"],
			Menu = _menu,
			IsVisible = true
		};

		// Load initial icon
		_trayIcon.Icon = AppIconProvider.GetIcon(false);

		// Double-click / click to show
		_trayIcon.Clicked += (_, _) => ShowMainWindow();

		// Subscribe to tracking state changes
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;
		SetActiveState(_worklogStateService.IsTracking);

		_isInitialized = true;
	}

	public void Show()
	{
		if (_trayIcon != null)
		{
			_trayIcon.IsVisible = true;
		}
	}

	public void Hide()
	{
		if (_trayIcon != null)
		{
			_trayIcon.IsVisible = false;
		}
	}

	public void RefreshFavoritesMenu()
	{
		if (_menu == null)
		{
			return;
		}

		// Remove existing favorites
		if (_favoritesSeparator != null)
		{
			_menu.Items.Remove(_favoritesSeparator);
			_favoritesSeparator = null;
		}
		foreach (var item in _favoriteMenuItems)
		{
			_menu.Items.Remove(item);
		}
		_favoriteMenuItems.Clear();

		var favorites = _settingsService.Settings.FavoriteWorkItems;
		if (favorites is { Count: > 0 })
		{
			// Insert separator after "New Work Entry" (index 2)
			_favoritesSeparator = new NativeMenuItemSeparator();
			var insertIndex = Math.Min(2, _menu.Items.Count);
			_menu.Items.Insert(insertIndex, _favoritesSeparator);
			insertIndex++;

			foreach (var favorite in favorites)
			{
				var icon = favorite.ShowAsTemplate ? "\u270E" : "\u2605";
				var menuItem = new NativeMenuItem($"{icon} {favorite.Name}");
				menuItem.Click += async (_, _) => await StartFavoriteWorkAsync(favorite);
				_menu.Items.Insert(insertIndex, menuItem);
				_favoriteMenuItems.Add(menuItem);
				insertIndex++;
			}
		}
	}

	public void Dispose()
	{
		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
		_cts.Cancel();
		_cts.Dispose();
		_trayIcon?.Dispose();
		_trayIcon = null;
		_isInitialized = false;
	}

	private void ShowMainWindow()
	{
		if (global::Avalonia.Application.Current?.ApplicationLifetime
			is IClassicDesktopStyleApplicationLifetime desktop)
		{
			var window = desktop.MainWindow;
			if (window != null)
			{
				window.Show();
				window.WindowState = WindowState.Normal;
				window.Activate();
			}
		}
	}

	private async Task OpenNewWorkEntryAsync()
	{
		try
		{
			await _dialogService.ShowNewWorkEntryDialogAsync();
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to open new work entry dialog from tray");
		}
	}

	private void SetActiveState(bool isActive)
	{
		if (_trayIcon == null)
		{
			return;
		}

		_trayIcon.ToolTipText = isActive
			? _localizationService["TrayTooltipActive"]
			: _localizationService["TrayTooltip"];

		_trayIcon.Icon = AppIconProvider.GetIcon(isActive) ?? _trayIcon.Icon;
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		SetActiveState(isTracking);
	}

	private async Task StartFavoriteWorkAsync(FavoriteWorkItem favorite)
	{
		try
		{
			if (favorite.ShowAsTemplate)
			{
				// Open dialog with pre-filled values for user to edit before starting
				await _dialogService.ShowNewWorkEntryDialogAsync(favorite.TicketId, favorite.Description);
				return;
			}

			if (_worklogStateService.IsTracking)
			{
				await _worklogStateService.StopTrackingAsync(_cts.Token);
			}
			await _worklogStateService.StartTrackingAsync(favorite.TicketId, favorite.Description, _cts.Token);
		}
		catch (OperationCanceledException) { /* Service is being disposed */ }
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to start favorite work '{FavoriteName}' from tray", favorite.Name);
		}
	}
}
