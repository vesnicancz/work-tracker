using Avalonia.Controls;
using Avalonia.Input;
using WorkTracker.Avalonia.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Views;

public partial class MainWindow : Window
{
	private ITrayIconService? _trayIconService;
	private ISettingsService? _settingsService;
	private IWorklogStateService? _worklogStateService;

	public MainWindow()
	{
		InitializeComponent();

		MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
		CloseButton.Click += (_, _) => Close();
		TitleBar.PointerPressed += OnTitleBarPointerPressed;

		Closing += OnWindowClosing;
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		PropertyChanged += OnWindowPropertyChanged;

		// Only resume timer if window is actually visible (skip when starting minimized)
		if (IsVisible)
		{
			(DataContext as MainViewModel)?.ResumeTimer();
		}
	}

	public void Initialize(MainViewModel viewModel, ITrayIconService trayIconService, ISettingsService settingsService, IWorklogStateService worklogStateService)
	{
		DataContext = viewModel;
		_trayIconService = trayIconService;
		_settingsService = settingsService;
		_worklogStateService = worklogStateService;

		// Swap loading indicator for main content
		LoadingPanel.IsVisible = false;
		MainContent.IsVisible = true;

		// Set application window icon
		Icon = AppIconProvider.GetIcon() ?? Icon;

		_trayIconService.Initialize();

		if (_settingsService.Settings.StartMinimized)
		{
			viewModel.PauseTimer();
			_trayIconService.Show();
		}
	}

	private void OnWindowPropertyChanged(object? sender, global::Avalonia.AvaloniaPropertyChangedEventArgs e)
	{
		if (e.Property == IsVisibleProperty && e.NewValue is true)
		{
			(DataContext as MainViewModel)?.ResumeTimer();
		}
	}

	private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
	{
		if (_settingsService?.Settings.CloseWindowBehavior == CloseWindowBehavior.MinimizeToTray)
		{
			e.Cancel = true;
			(DataContext as MainViewModel)?.PauseTimer();
			Hide();
			_trayIconService?.Show();
		}
		else
		{
			(DataContext as IDisposable)?.Dispose();
			_trayIconService?.Dispose();
		}
	}

	private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
	{
		if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
		{
			BeginMoveDrag(e);
		}
	}

	private void OnDataGridDoubleTapped(object? sender, TappedEventArgs e)
	{
		if (DataContext is MainViewModel vm && vm.SelectedWorkEntry is WorkEntry entry)
		{
			if (vm.EditWorkEntryCommand.CanExecute(entry))
			{
				vm.EditWorkEntryCommand.Execute(entry);
			}
		}
	}

}
