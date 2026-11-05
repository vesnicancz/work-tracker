using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Domain.Entities;
using WorkTracker.Avalonia;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Services;

public sealed class DialogService : IDialogService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IMainWindowAccessor _mainWindowAccessor;
	private readonly ILogger<DialogService> _logger;

	// At most one new/edit work entry dialog at a time. Only touched on the UI thread.
	private WorkEntryEditDialog? _openWorkEntryDialog;

	internal WorkEntryEditDialog? OpenWorkEntryDialog => _openWorkEntryDialog;

	// The message box most recently opened. Only touched on the UI thread.
	private MessageBoxWindow? _openMessageBox;

	internal MessageBoxWindow? OpenMessageBox => _openMessageBox;

	public DialogService(IServiceScopeFactory scopeFactory, IMainWindowAccessor mainWindowAccessor, ILogger<DialogService> logger)
	{
		_scopeFactory = scopeFactory;
		_mainWindowAccessor = mainWindowAccessor;
		_logger = logger;
	}

	public Task<bool> ShowEditWorkEntryDialogAsync(WorkEntry workEntry)
	{
		return ShowWorkEntryDialogCoreAsync(workEntry);
	}

	public Task<bool> ShowNewWorkEntryDialogAsync(string? ticketId = null, string? description = null, DateTime? date = null, DateTime? startTime = null, DateTime? endTime = null)
	{
		return ShowWorkEntryDialogCoreAsync(null, ticketId, description, date, startTime, endTime);
	}

	private async Task<bool> ShowWorkEntryDialogCoreAsync(WorkEntry? workEntry, string? templateTicketId = null, string? templateDescription = null, DateTime? date = null, DateTime? startTime = null, DateTime? endTime = null)
	{
		// The global hotkey and the tray menu are not blocked by a modal dialog, so pressing the
		// hotkey twice would otherwise stack a second dialog on top of the first. Bring the open
		// one forward instead; whatever it was opened with stays, nothing new has been saved.
		if (_openWorkEntryDialog is { } openDialog)
		{
			_logger.LogDebug("Work entry dialog already open, activating it");
			openDialog.Activate();
			return false;
		}

		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<WorkEntryEditViewModel>();

		if (workEntry != null)
		{
			viewModel.InitializeForEdit(workEntry);
		}
		else
		{
			viewModel.InitializeForNew(templateTicketId, templateDescription, date, startTime, endTime);
		}

		var dialog = new WorkEntryEditDialog { DataContext = viewModel };
		_openWorkEntryDialog = dialog;
		dialog.Closed += (_, _) => _openWorkEntryDialog = null;

		var mainWindow = GetVisibleMainWindow();
		if (mainWindow != null)
		{
			var result = await dialog.ShowDialog<bool?>(mainWindow);
			return result == true;
		}

		// Main window hidden (e.g. minimized to tray) — show as standalone window
		var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
		dialog.Closed += (_, _) => tcs.TrySetResult(viewModel.DialogResult);
		dialog.Show();
		dialog.Activate();
		return await tcs.Task;
	}

	public async Task ShowSubmitWorklogDialogAsync(DateTime? date = null, bool isWeek = false)
	{
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SubmitWorklogViewModel>();
		await viewModel.InitializeAsync(date, isWeek);

		var dialog = new SubmitWorklogDialog { DataContext = viewModel };
		var mainWindow = _mainWindowAccessor.MainWindow;
		if (mainWindow != null)
		{
			await dialog.ShowDialog(mainWindow);
		}
	}

	public Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
	{
		return ShowMessageBoxAsync(title, message, MessageBoxButtons.YesNo);
	}

	public Task ShowErrorAsync(string message, string title = "Error")
	{
		return ShowMessageBoxAsync(title, message, MessageBoxButtons.Ok);
	}

	public Task ShowInformationAsync(string message, string title = "Information")
	{
		return ShowMessageBoxAsync(title, message, MessageBoxButtons.Ok);
	}

	/// <summary>
	/// Shows a message on top of whatever asked for it. A window can't own a dialog while it is hidden,
	/// and the application in the tray has a hidden main window: the hotkey or a remote controller opens
	/// the work entry dialog on its own there, and that dialog can still need an answer about an overlap.
	/// </summary>
	private async Task<bool> ShowMessageBoxAsync(string title, string message, MessageBoxButtons buttons)
	{
		var dialog = new MessageBoxWindow(title, message, buttons);
		_openMessageBox = dialog;
		dialog.Closed += (_, _) => _openMessageBox = null;

		var owner = _openWorkEntryDialog is { IsVisible: true } workEntryDialog ? workEntryDialog : GetVisibleMainWindow();
		if (owner != null)
		{
			await dialog.ShowDialog(owner);
			return dialog.Result;
		}

		// Without an owner the dialog defaults would hide it off-centre and off the taskbar
		dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
		dialog.ShowInTaskbar = true;

		var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		dialog.Closed += (_, _) => closed.TrySetResult();
		dialog.Show();
		dialog.Activate();
		await closed.Task;

		return dialog.Result;
	}

	public async Task<bool> ShowSettingsDialogAsync()
	{
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SettingsViewModel>();

		var dialog = new SettingsWindow { DataContext = viewModel };
		var mainWindow = _mainWindowAccessor.MainWindow;
		if (mainWindow != null)
		{
			var result = await dialog.ShowDialog<bool?>(mainWindow);
			return result == true;
		}
		return false;
	}

	public async Task<WorkSuggestionViewModel?> ShowSuggestionsDialogAsync(DateTime selectedDate)
	{
		using var scope = _scopeFactory.CreateScope();
		var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkSuggestionOrchestrator>();
		var cache = scope.ServiceProvider.GetRequiredService<IWorkSuggestionCache>();
		var viewState = scope.ServiceProvider.GetRequiredService<ISuggestionsViewState>();
		var timeProvider = scope.ServiceProvider.GetRequiredService<TimeProvider>();
		using var viewModel = new SuggestionsViewModel(orchestrator, cache, viewState, timeProvider);

		var dialog = new SuggestionsWindow();
		dialog.BindViewModel(viewModel);

		// Show dialog immediately with loading indicator, load data in background.
		_ = viewModel.InitializeAsync(selectedDate)
			.SafeFireAndForgetAsync(ex => _logger.LogWarning(ex, "Suggestions initialization failed"));

		var mainWindow = GetVisibleMainWindow();
		if (mainWindow != null)
		{
			await dialog.ShowDialog(mainWindow);
		}
		else
		{
			// Main window hidden (e.g. minimized to tray) — show as standalone window
			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			dialog.Closed += (_, _) => tcs.TrySetResult(true);
			dialog.Show();
			dialog.Activate();
			await tcs.Task;
		}

		return dialog.SelectedSuggestion;
	}

	private Window? GetVisibleMainWindow()
	{
		return _mainWindowAccessor.MainWindow is { IsVisible: true } mainWindow ? mainWindow : null;
	}
}
