using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;
using WorkTracker.WPF.ViewModels;
using WorkTracker.WPF.Views;

namespace WorkTracker.WPF.Services;

/// <summary>
/// Implementation of IDialogService using WPF dialogs
/// Creates a new scope for each dialog to ensure proper service lifetimes
/// </summary>
public sealed class DialogService : IDialogService
{
	private readonly IServiceScopeFactory _scopeFactory;

	public DialogService(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public Task<bool> ShowEditWorkEntryDialogAsync(WorkEntry workEntry)
	{
		return ShowWorkEntryDialogCoreAsync(workEntry);
	}

	public Task<bool> ShowNewWorkEntryDialogAsync(string? ticketId = null, string? description = null, DateTime? date = null, DateTime? startTime = null, DateTime? endTime = null)
	{
		return ShowWorkEntryDialogCoreAsync(null, ticketId, description, date, startTime, endTime);
	}

	private Task<bool> ShowWorkEntryDialogCoreAsync(WorkEntry? workEntry, string? templateTicketId = null, string? templateDescription = null, DateTime? date = null, DateTime? startTime = null, DateTime? endTime = null)
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

		var dialog = new WorkEntryEditDialog
		{
			DataContext = viewModel,
			Owner = System.Windows.Application.Current.MainWindow
		};

		return Task.FromResult(dialog.ShowDialog() == true);
	}

	public async Task<bool> ShowSubmitWorklogDialogAsync(DateTime? date = null, bool isWeek = false)
	{
		// Create a new scope for this dialog operation
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SubmitWorklogViewModel>();
		await viewModel.InitializeAsync(date, isWeek);

		var dialog = new SubmitWorklogDialog
		{
			DataContext = viewModel,
			Owner = System.Windows.Application.Current.MainWindow
		};

		return dialog.ShowDialog() == true;
	}

	public Task<bool> ShowConfirmationAsync(string message, string title = "Confirm")
	{
		var result = MessageBox.Show(
			message,
			title,
			MessageBoxButton.YesNo,
			MessageBoxImage.Question);

		return Task.FromResult(result == MessageBoxResult.Yes);
	}

	public Task ShowErrorAsync(string message, string title = "Error")
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
		return Task.CompletedTask;
	}

	public Task ShowInformationAsync(string message, string title = "Information")
	{
		MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
		return Task.CompletedTask;
	}

	public Task<WorkSuggestionViewModel?> ShowSuggestionsDialogAsync(DateTime selectedDate)
	{
		using var scope = _scopeFactory.CreateScope();
		var orchestrator = scope.ServiceProvider.GetRequiredService<IWorkSuggestionOrchestrator>();
		using var viewModel = new SuggestionsViewModel(orchestrator);

		var dialog = new SuggestionsWindow();
		dialog.BindViewModel(viewModel);

		_ = viewModel.InitializeAsync(selectedDate).ContinueWith(t =>
		{
			if (t.IsFaulted)
			{
				System.Diagnostics.Debug.WriteLine($"Suggestions init failed: {t.Exception?.InnerException?.Message}");
			}
		}, TaskScheduler.Default);
		dialog.Owner = System.Windows.Application.Current.MainWindow;
		dialog.ShowDialog();

		return Task.FromResult(dialog.SelectedSuggestion);
	}

	public Task<bool> ShowSettingsDialogAsync()
	{
		// Create a new scope for this dialog operation
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<SettingsViewModel>();

		var dialog = new SettingsWindow
		{
			DataContext = viewModel,
			Owner = System.Windows.Application.Current.MainWindow
		};

		return Task.FromResult(dialog.ShowDialog() == true);
	}
}