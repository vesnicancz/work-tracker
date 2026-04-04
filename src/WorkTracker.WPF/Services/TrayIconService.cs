using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontAwesome6.Svg;
using Hardcodet.Wpf.TaskbarNotification;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.WPF.Services;

public sealed class TrayIconService : ITrayIconService, IDisposable
{
	private readonly IDialogService _dialogService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly ISettingsService _settingsService;
	private readonly ILocalizationService _localizationService;
	private TaskbarIcon? _taskbarIcon;
	private bool _isInitialized;
	private ResourceDictionary? _menuStyles;
	private int _favoritesInsertIndex;
	private readonly List<MenuItem> _favoriteMenuItems = new();
	private readonly CancellationTokenSource _cts = new();
	private Separator? _favoritesSeparator;
	private MenuItem? _stopWorkItem;

	public TrayIconService(
		IDialogService dialogService,
		IWorklogStateService worklogStateService,
		ISettingsService settingsService,
		ILocalizationService localizationService)
	{
		_dialogService = dialogService;
		_worklogStateService = worklogStateService;
		_settingsService = settingsService;
		_localizationService = localizationService;
	}

	public void Initialize()
	{
		if (_isInitialized)
		{
			return;
		}

		_taskbarIcon = new TaskbarIcon
		{
			ToolTipText = _localizationService["TrayTooltip"]
		};

		// Load initial icon
		var initialIcon = AppIconProvider.GetTrayIcon(false);
		if (initialIcon is not null)
		{
			_taskbarIcon.Icon = initialIcon;
		}

		// Load tray menu styles
		var stylesUri = new Uri("pack://application:,,,/Resources/Styles/TrayMenuStyles.xaml", UriKind.Absolute);
		_menuStyles = new ResourceDictionary { Source = stylesUri };

		// Create context menu
		var contextMenu = new ContextMenu();
		contextMenu.Resources.MergedDictionaries.Add(_menuStyles);
		contextMenu.Style = (Style)_menuStyles["TrayContextMenuStyle"];

		var showMenuItem = new MenuItem
		{
			Header = _localizationService["TrayShow"],
			Style = (Style)_menuStyles["TrayMenuItemStyle"],
			Icon = CreateFontAwesomeIcon("Solid_WindowRestore", Brushes.DarkSlateGray)
		};
		showMenuItem.Click += (s, e) => ShowMainWindow();
		contextMenu.Items.Add(showMenuItem);

		var newEntryMenuItem = new MenuItem
		{
			Header = _localizationService["TrayNewWorkEntry"],
			Style = (Style)_menuStyles["TrayMenuItemStyle"],
			Icon = CreateFontAwesomeIcon("Solid_Plus", Brushes.Green)
		};
		newEntryMenuItem.Click += async (s, e) => await OpenNewWorkEntryAsync();
		contextMenu.Items.Add(newEntryMenuItem);

		_stopWorkItem = new MenuItem
		{
			Header = _localizationService["TrayStopWork"],
			Style = (Style)_menuStyles["TrayMenuItemStyle"],
			Icon = CreateFontAwesomeIcon("Solid_Stop", Brushes.OrangeRed),
			IsEnabled = _worklogStateService.IsTracking
		};
		_stopWorkItem.Click += async (s, e) => await StopWorkAsync();
		contextMenu.Items.Add(_stopWorkItem);

		// Remember position for favorites (they will be inserted here)
		_favoritesInsertIndex = contextMenu.Items.Count;

		// Add favorites directly to menu
		RefreshFavoritesMenu();

		var separator = new Separator
		{
			Style = (Style)_menuStyles["TrayMenuSeparatorStyle"]
		};
		contextMenu.Items.Add(separator);

		var exitMenuItem = new MenuItem
		{
			Header = _localizationService["TrayExit"],
			Style = (Style)_menuStyles["TrayMenuItemStyle"],
			Icon = CreateFontAwesomeIcon("Solid_PowerOff", Brushes.Crimson)
		};
		exitMenuItem.Click += (s, e) => System.Windows.Application.Current?.Shutdown();
		contextMenu.Items.Add(exitMenuItem);

		_taskbarIcon.ContextMenu = contextMenu;

		// Click to toggle window visibility
		_taskbarIcon.TrayLeftMouseUp += (s, e) => ToggleMainWindow();

		// Subscribe to tracking state changes
		_worklogStateService.IsTrackingChanged += OnIsTrackingChanged;

		// Set initial icon state
		SetActiveState(_worklogStateService.IsTracking);

		_isInitialized = true;
	}

	public void Show()
	{
		if (_taskbarIcon != null)
		{
			_taskbarIcon.Visibility = Visibility.Visible;
		}
	}

	public void Hide()
	{
		if (_taskbarIcon != null)
		{
			_taskbarIcon.Visibility = Visibility.Collapsed;
		}
	}

	public void Dispose()
	{
		// Unsubscribe from events
		_worklogStateService.IsTrackingChanged -= OnIsTrackingChanged;

		_cts.Cancel();
		_cts.Dispose();
		_taskbarIcon?.Dispose();
		_taskbarIcon = null;
		_isInitialized = false;
	}

	private void ShowMainWindow()
	{
		var mainWindow = System.Windows.Application.Current.MainWindow;
		if (mainWindow != null)
		{
			mainWindow.Show();
			mainWindow.WindowState = WindowState.Normal;
			mainWindow.Activate();
		}
	}

	private void ToggleMainWindow()
	{
		var mainWindow = System.Windows.Application.Current.MainWindow;
		if (mainWindow == null)
		{
			return;
		}

		if (mainWindow.IsVisible && mainWindow.WindowState != WindowState.Minimized)
		{
			mainWindow.WindowState = WindowState.Minimized;
		}
		else
		{
			ShowMainWindow();
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
			MessageBox.Show(
				_localizationService.GetFormattedString("TrayOpenDialogError", ex.Message),
				_localizationService["Error"],
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
	}

	private SvgAwesome CreateFontAwesomeIcon(string iconName, Brush color)
	{
		var icon = new SvgAwesome
		{
			Width = 16,
			Height = 16,
			PrimaryColor = color
		};

		// Set icon using reflection to avoid enum issues
		var iconProperty = typeof(SvgAwesome).GetProperty("Icon");
		if (iconProperty != null)
		{
			var enumType = iconProperty.PropertyType;
			var iconValue = Enum.Parse(enumType, iconName);
			iconProperty.SetValue(icon, iconValue);
		}

		return icon;
	}

	private void SetActiveState(bool isActive)
	{
		if (_taskbarIcon == null)
		{
			return;
		}

		_taskbarIcon.Icon = AppIconProvider.GetTrayIcon(isActive) ?? _taskbarIcon.Icon;
		_taskbarIcon.ToolTipText = isActive
			? _localizationService["TrayTooltipActive"]
			: _localizationService["TrayTooltip"];

		_stopWorkItem!.IsEnabled = isActive;
	}

	private void OnIsTrackingChanged(object? sender, bool isTracking)
	{
		// Update tray icon when tracking state changes
		SetActiveState(isTracking);
	}

	public void RefreshFavoritesMenu()
	{
		if (_taskbarIcon?.ContextMenu == null || _menuStyles == null)
		{
			return;
		}

		var contextMenu = _taskbarIcon.ContextMenu;

		// Remove existing separator for favorites
		if (_favoritesSeparator != null)
		{
			contextMenu.Items.Remove(_favoritesSeparator);
			_favoritesSeparator = null;
		}

		// Remove existing favorite menu items
		foreach (var item in _favoriteMenuItems)
		{
			contextMenu.Items.Remove(item);
		}
		_favoriteMenuItems.Clear();

		var favorites = _settingsService.Settings.FavoriteWorkItems;

		if (favorites != null && favorites.Count > 0)
		{
			// Insert separator before favorites
			var insertIndex = _favoritesInsertIndex;
			_favoritesSeparator = new Separator
			{
				Style = (Style)_menuStyles["TrayMenuSeparatorStyle"]
			};
			contextMenu.Items.Insert(insertIndex, _favoritesSeparator);
			insertIndex++;

			// Insert favorites
			foreach (var favorite in favorites)
			{
				var menuItem = new MenuItem
				{
					Header = favorite.Name,
					Style = (Style)_menuStyles["TrayMenuItemStyle"],
					Icon = favorite.ShowAsTemplate
						? CreateFontAwesomeIcon("Solid_PenToSquare", Brushes.DodgerBlue)
						: CreateFontAwesomeIcon("Solid_Star", Brushes.Gold),
					Tag = favorite
				};
				menuItem.Click += async (s, e) => await StartFavoriteWorkAsync(favorite);

				contextMenu.Items.Insert(insertIndex, menuItem);
				_favoriteMenuItems.Add(menuItem);
				insertIndex++;
			}
		}
	}

	private async Task StopWorkAsync()
	{
		try
		{
			var result = await _worklogStateService.StopTrackingAsync(_cts.Token);
			if (result.IsFailure)
			{
				MessageBox.Show(
					_localizationService.GetFormattedString("FailedToStopWork", result.Error ?? "Unknown error"),
					_localizationService["Error"],
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}
		catch (OperationCanceledException) { /* Service is being disposed */ }
		catch (Exception ex)
		{
			MessageBox.Show(
				_localizationService.GetFormattedString("FailedToStopWork", ex.Message),
				_localizationService["Error"],
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
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
				MessageBox.Show(
					_localizationService.GetFormattedString("FailedToStartWork", result.Error ?? "Unknown error"),
					_localizationService["Error"],
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}
		catch (OperationCanceledException) { /* Service is being disposed */ }
		catch (Exception ex)
		{
			MessageBox.Show(
				_localizationService.GetFormattedString("FailedToStartWork", ex.Message),
				_localizationService["Error"],
				MessageBoxButton.OK,
				MessageBoxImage.Error);
		}
	}
}