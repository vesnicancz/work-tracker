using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Services;
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

	public async Task<bool> ShowEditWorkEntryDialogAsync(WorkEntry? workEntry = null)
	{
		// Create a new scope for this dialog operation
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<WorkEntryEditViewModel>();
		await viewModel.InitializeAsync(workEntry);

		var dialog = new WorkEntryEditDialog
		{
			DataContext = viewModel,
			Owner = System.Windows.Application.Current.MainWindow
		};

		return dialog.ShowDialog() == true;
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