using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FontAwesome6.Svg;
using Hardcodet.Wpf.TaskbarNotification;
using WorkTracker.WPF.Models;

namespace WorkTracker.WPF.Services;

public sealed class TrayIconService : ITrayIconService, IDisposable
{
	private readonly IDialogService _dialogService;
	private readonly IWorklogStateService _worklogStateService;
	private readonly ISettingsService _settingsService;
	private readonly LocalizationService _localizationService;
	private TaskbarIcon? _taskbarIcon;
	private bool _isInitialized;
	private System.Windows.Media.ImageSource? _inactiveIcon;
	private System.Windows.Media.ImageSource? _activeIcon;
	private ResourceDictionary? _menuStyles;
	private int _favoritesInsertIndex;
	private readonly List<MenuItem> _favoriteMenuItems = new();
	private Separator? _favoritesSeparator;

	public TrayIconService(
		IDialogService dialogService,
		IWorklogStateService worklogStateService,
		ISettingsService settingsService)
	{
		_dialogService = dialogService;
		_worklogStateService = worklogStateService;
		_settingsService = settingsService;
		_localizationService = LocalizationService.Instance;
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

		// Try to load icons, or use default application icon
		try
		{
			var inactiveIconUri = new Uri("pack://application:,,,/Resources/icon.ico", UriKind.Absolute);
			_inactiveIcon = new System.Windows.Media.Imaging.BitmapImage(inactiveIconUri);

			// Try to load active icon, fallback to inactive if not found
			try
			{
				var activeIconUri = new Uri("pack://application:,,,/Resources/icon-active.ico", UriKind.Absolute);
				_activeIcon = new System.Windows.Media.Imaging.BitmapImage(activeIconUri);
			}
			catch
			{
				// Use inactive icon as fallback for active state
				_activeIcon = _inactiveIcon;
			}

			_taskbarIcon.IconSource = _inactiveIcon;
		}
		catch
		{
			// Use application icon as fallback
			if (System.Windows.Application.Current.MainWindow?.Icon != null)
			{
				_inactiveIcon = System.Windows.Application.Current.MainWindow.Icon;
				_activeIcon = _inactiveIcon;
				_taskbarIcon.IconSource = _inactiveIcon;
			}
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
		exitMenuItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
		contextMenu.Items.Add(exitMenuItem);

		_taskbarIcon.ContextMenu = contextMenu;

		// Double-click to show window
		_taskbarIcon.TrayLeftMouseUp += (s, e) => ShowMainWindow();

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

	private async Task OpenNewWorkEntryAsync()
	{
		try
		{
			// Show the work entry dialog (null = new entry)
			await _dialogService.ShowEditWorkEntryDialogAsync(null);
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

		_taskbarIcon.IconSource = isActive ? _activeIcon : _inactiveIcon;
		_taskbarIcon.ToolTipText = isActive
			? _localizationService["TrayTooltipActive"]
			: _localizationService["TrayTooltip"];
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
					Icon = CreateFontAwesomeIcon("Solid_Star", Brushes.Gold),
					Tag = favorite
				};
				menuItem.Click += async (s, e) => await StartFavoriteWorkAsync(favorite);

				contextMenu.Items.Insert(insertIndex, menuItem);
				_favoriteMenuItems.Add(menuItem);
				insertIndex++;
			}
		}
	}

	private async Task StartFavoriteWorkAsync(FavoriteWorkItem favorite)
	{
		try
		{
			// Stop current tracking if active
			if (_worklogStateService.IsTracking)
			{
				await _worklogStateService.StopTrackingAsync();
			}

			// Start tracking with favorite's ticket and description
			var result = await _worklogStateService.StartTrackingAsync(favorite.TicketId, favorite.Description);

			if (!result.IsSuccess)
			{
				MessageBox.Show(
					_localizationService.GetFormattedString("FailedToStartWork", result.Error ?? "Unknown error"),
					_localizationService["Error"],
					MessageBoxButton.OK,
					MessageBoxImage.Error);
			}
		}
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