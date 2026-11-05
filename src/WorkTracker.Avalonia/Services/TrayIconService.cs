using System.Collections.Concurrent;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.Views;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Services;

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
	private readonly IActionFeedbackService _actionFeedback;
	private readonly IMainWindowAccessor _mainWindowAccessor;
	private TrayIcon? _trayIcon;
	private NativeMenu? _menu;
	private bool _isInitialized;
	private bool _disposed;
	private readonly List<NativeMenuItem> _favoriteMenuItems = new();
	private readonly CancellationTokenSource _cts = new();
	private NativeMenuItemSeparator? _favoritesSeparator;
	private NativeMenuItem? _stopWorkItem;
	private NativeMenuItem? _showItem;
	private NativeMenuItem? _newEntryItem;
	private NativeMenuItem? _exitItem;
	private int _favoritesInsertIndex;

	public TrayIconService(
		IDialogService dialogService,
		IWorklogStateService worklogStateService,
		ISettingsService settingsService,
		ILocalizationService localizationService,
		IActionFeedbackService actionFeedback,
		IMainWindowAccessor mainWindowAccessor,
		ILogger<TrayIconService> logger)
	{
		_actionFeedback = actionFeedback;
		_mainWindowAccessor = mainWindowAccessor;
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
		_showItem = new NativeMenuItem(_localizationService["TrayShow"]);
		_showItem.Icon = RenderMenuIcon(MaterialIconKind.WindowRestore, Brushes.DarkSlateGray);
		_showItem.Click += (_, _) => _mainWindowAccessor.BringForward();
		_menu.Items.Add(_showItem);

		// New work entry
		_newEntryItem = new NativeMenuItem(_localizationService["TrayNewWorkEntry"]);
		_newEntryItem.Icon = RenderMenuIcon(MaterialIconKind.Plus, Brushes.Green);
		_newEntryItem.Click += async (_, _) => await OpenNewWorkEntryAsync();
		_menu.Items.Add(_newEntryItem);

		// Stop work
		_stopWorkItem = new NativeMenuItem(_localizationService["TrayStopWork"]);
		_stopWorkItem.Icon = RenderMenuIcon(MaterialIconKind.Stop, Brushes.OrangeRed);
		_stopWorkItem.IsEnabled = _worklogStateService.IsTracking;
		_stopWorkItem.Click += async (_, _) => await StopWorkAsync();
		_menu.Items.Add(_stopWorkItem);

		// Remember position for favorites (they will be inserted here)
		_favoritesInsertIndex = _menu.Items.Count;

		// Add favorites directly to menu
		RefreshFavoritesMenu();

		// Separator before exit
		_menu.Items.Add(new NativeMenuItemSeparator());

		// Exit
		_exitItem = new NativeMenuItem(_localizationService["TrayExit"]);
		_exitItem.Icon = RenderMenuIcon(MaterialIconKind.Power, Brushes.Crimson);
		_exitItem.Click += (_, _) => _mainWindowAccessor.RequestShutdown();
		_menu.Items.Add(_exitItem);

		_trayIcon = new TrayIcon
		{
			ToolTipText = _localizationService["TrayTooltip"],
			Menu = _menu,
			IsVisible = true
		};

		// Load initial icon
		_trayIcon.Icon = AppIconProvider.GetTrayIcon(false);

		// Click to toggle window visibility
		_trayIcon.Clicked += (_, _) => ToggleMainWindow();

		// Subscribe to tracking state changes
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;
		SetActiveState(_worklogStateService.IsTracking);

		_localizationService.PropertyChanged += OnLocalizationChanged;

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
			var insertIndex = _favoritesInsertIndex;
			_favoritesSeparator = new NativeMenuItemSeparator();
			_menu.Items.Insert(insertIndex, _favoritesSeparator);
			insertIndex++;

			foreach (var favorite in favorites)
			{
				var menuItem = new NativeMenuItem(favorite.Name);
				menuItem.Icon = favorite.ShowAsTemplate
					? RenderMenuIcon(MaterialIconKind.SquareEditOutline, Brushes.DodgerBlue)
					: RenderMenuIcon(MaterialIconKind.Star, Brushes.Gold);
				menuItem.Click += async (_, _) => await StartFavoriteWorkAsync(favorite);
				_menu.Items.Insert(insertIndex, menuItem);
				_favoriteMenuItems.Add(menuItem);
				insertIndex++;
			}
		}
	}

	public void Dispose()
	{
		// Called both from MainWindow.OnWindowClosing and from host/container disposal —
		// the second call must not touch the already-disposed CancellationTokenSource
		if (_disposed)
		{
			return;
		}
		_disposed = true;

		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;
		_localizationService.PropertyChanged -= OnLocalizationChanged;
		_cts.Cancel();
		_cts.Dispose();
		_trayIcon?.Dispose();
		_trayIcon = null;
		_isInitialized = false;
	}

	private void ToggleMainWindow()
	{
		var window = _mainWindowAccessor.MainWindow;
		if (window == null)
		{
			return;
		}

		if (window.IsVisible && window.WindowState != WindowState.Minimized)
		{
			if (window is MainWindow mainWindow)
			{
				mainWindow.HideToTray();
			}
			else
			{
				window.Hide();
			}
		}
		else
		{
			_mainWindowAccessor.BringForward();
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
			await ReportFailureAsync(_localizationService.GetFormattedString("TrayOpenDialogError", ex.Message));
		}
	}

	/// <summary>
	/// A menu click has no other way to show that it failed. The token source is gone once the service
	/// is disposed, and by then nobody is looking.
	/// </summary>
	private Task ReportFailureAsync(string message)
	{
		return _disposed ? Task.CompletedTask : _actionFeedback.ReportFailureAsync(message, _cts.Token);
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

		_trayIcon.Icon = AppIconProvider.GetTrayIcon(isActive) ?? _trayIcon.Icon;

		_stopWorkItem!.IsEnabled = isActive;
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		SetActiveState(isTracking);
	}

	/// <summary>
	/// Tray menu headers are native strings captured when the menu was built, so a live language
	/// switch has to push the new text into them. Reacts only to the "all changed" signal - the
	/// service also raises the indexer name for XAML bindings, which would double-fire this.
	/// </summary>
	private void OnLocalizationChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!string.IsNullOrEmpty(e.PropertyName))
		{
			return;
		}

		if (Dispatcher.UIThread.CheckAccess())
		{
			RefreshMenuTexts();
		}
		else
		{
			Dispatcher.UIThread.Post(RefreshMenuTexts);
		}
	}

	private void RefreshMenuTexts()
	{
		if (_disposed || _menu == null)
		{
			return;
		}

		if (_showItem != null)
		{
			_showItem.Header = _localizationService["TrayShow"];
		}

		if (_newEntryItem != null)
		{
			_newEntryItem.Header = _localizationService["TrayNewWorkEntry"];
		}

		if (_stopWorkItem != null)
		{
			_stopWorkItem.Header = _localizationService["TrayStopWork"];
		}

		if (_exitItem != null)
		{
			_exitItem.Header = _localizationService["TrayExit"];
		}

		// Re-applies the tooltip in the new language.
		SetActiveState(_worklogStateService.IsTracking);
	}

	private async Task StopWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StopTrackingAsync(_cts.Token);
			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to stop work from tray: {Error}", result.Error);
				await ReportFailureAsync(_localizationService.GetFormattedString("TrayStopWorkFailed", result.Error));
			}
		}
		catch (OperationCanceledException) { /* Service is being disposed */ }
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to stop work from tray");
			await ReportFailureAsync(_localizationService.GetFormattedString("TrayStopWorkFailed", ex.Message));
		}
	}

	// Cache lives for process lifetime; bitmaps are intentionally never disposed
	// because NativeMenuItems hold references to them.
	private static readonly ConcurrentDictionary<(MaterialIconKind, uint, int), Bitmap> s_iconCache = new();

	private static Bitmap RenderMenuIcon(MaterialIconKind kind, ISolidColorBrush foreground, int size = 16)
	{
		var cacheKey = (kind, foreground.Color.ToUInt32(), size);
		return s_iconCache.GetOrAdd(cacheKey, _ =>
		{
			var pathData = MaterialIconDataProvider.GetData(kind);
			var geometry = Geometry.Parse(pathData);

			var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
			using (var context = bitmap.CreateDrawingContext())
			{
				// Material icons use a 24x24 viewbox
				var scale = size / 24.0;
				using (context.PushTransform(Matrix.CreateScale(scale, scale)))
				{
					context.DrawGeometry(foreground, null, geometry);
				}
			}

			return bitmap;
		});
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

			var result = await _worklogStateService.StartTrackingAsync(favorite.TicketId, favorite.Description, _cts.Token);
			if (result.IsFailure)
			{
				_logger.LogWarning("Failed to start favorite work from tray: {Error}", result.Error);
				await ReportFailureAsync(_localizationService.GetFormattedString("TrayStartFavoriteFailed", favorite.Name, result.Error));
			}
		}
		catch (OperationCanceledException) { /* Service is being disposed */ }
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to start favorite work '{FavoriteName}' from tray", favorite.Name);
			await ReportFailureAsync(_localizationService.GetFormattedString("TrayStartFavoriteFailed", favorite.Name, ex.Message));
		}
	}
}
