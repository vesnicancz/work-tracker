using Avalonia.Controls;
using Avalonia.Input;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Views;

public partial class MainWindow : Window
{
	private ITrayIconService? _trayIconService;
	private ISettingsService? _settingsService;

	public MainWindow()
	{
		InitializeComponent();

		MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
		CloseButton.Click += (_, _) => Close();
		TitleBar.PointerPressed += OnTitleBarPointerPressed;

		Closing += OnWindowClosing;
	}

	public void Initialize(MainViewModel viewModel, ITrayIconService trayIconService, ISettingsService settingsService)
	{
		DataContext = viewModel;
		_trayIconService = trayIconService;
		_settingsService = settingsService;

		_trayIconService.Initialize();

		if (_settingsService.Settings.StartMinimized)
		{
			viewModel.PauseTimer();
			Hide();
			_trayIconService.Show();
		}
	}

	protected override void OnOpened(EventArgs e)
	{
		base.OnOpened(e);
		PropertyChanged += OnWindowPropertyChanged;
		(DataContext as MainViewModel)?.ResumeTimer();
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
