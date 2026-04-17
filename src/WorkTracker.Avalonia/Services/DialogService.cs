using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Services;

public sealed class DialogService : IDialogService
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ILogger<DialogService> _logger;

	public DialogService(IServiceScopeFactory scopeFactory, ILogger<DialogService> logger)
	{
		_scopeFactory = scopeFactory;
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

	public async Task<bool> ShowSubmitWorklogDialogAsync(DateTime? date = null, bool isWeek = false)
	{
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SubmitWorklogViewModel>();
		await viewModel.InitializeAsync(date, isWeek);

		var dialog = new SubmitWorklogDialog { DataContext = viewModel };
		var mainWindow = GetMainWindow();
		if (mainWindow != null)
		{
			var result = await dialog.ShowDialog<bool?>(mainWindow);
			return result == true;
		}
		return false;
	}

	public async Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
	{
		var dialog = new MessageBoxWindow(title, message, true);
		var mainWindow = GetMainWindow();
		if (mainWindow != null)
		{
			var result = await dialog.ShowDialog<bool?>(mainWindow);
			return result == true;
		}
		return false;
	}

	public async Task ShowErrorAsync(string message, string title = "Error")
	{
		var dialog = new MessageBoxWindow(title, message, false);
		var mainWindow = GetMainWindow();
		if (mainWindow != null)
		{
			await dialog.ShowDialog(mainWindow);
		}
	}

	public async Task ShowInformationAsync(string message, string title = "Information")
	{
		var dialog = new MessageBoxWindow(title, message, false);
		var mainWindow = GetMainWindow();
		if (mainWindow != null)
		{
			await dialog.ShowDialog(mainWindow);
		}
	}

	public async Task<bool> ShowSettingsDialogAsync()
	{
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SettingsViewModel>();

		var dialog = new SettingsWindow { DataContext = viewModel };
		var mainWindow = GetMainWindow();
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
		using var viewModel = new SuggestionsViewModel(orchestrator, cache);

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

	private static Window? GetVisibleMainWindow()
	{
		if (global::Avalonia.Application.Current?.ApplicationLifetime
				is IClassicDesktopStyleApplicationLifetime { MainWindow: { IsVisible: true } mainWindow })
		{
			return mainWindow;
		}
		return null;
	}

	private static Window? GetMainWindow()
	{
		if (global::Avalonia.Application.Current?.ApplicationLifetime
				is IClassicDesktopStyleApplicationLifetime desktop)
		{
			return desktop.MainWindow;
		}
		return null;
	}
}