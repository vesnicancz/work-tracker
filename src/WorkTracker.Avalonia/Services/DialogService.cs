using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Domain.Entities;
using WorkTracker.UI.Shared.Services;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Avalonia.Views;

namespace WorkTracker.Avalonia.Services;

public sealed class DialogService : IDialogService
{
	private readonly IServiceScopeFactory _scopeFactory;

	public DialogService(IServiceScopeFactory scopeFactory)
	{
		_scopeFactory = scopeFactory;
	}

	public async Task<bool> ShowEditWorkEntryDialogAsync(WorkEntry? workEntry = null)
	{
		using var scope = _scopeFactory.CreateScope();
		var viewModel = scope.ServiceProvider.GetRequiredService<WorkEntryEditViewModel>();
		await viewModel.InitializeAsync(workEntry);

		var dialog = new WorkEntryEditDialog { DataContext = viewModel };
		var mainWindow = GetMainWindow();
		if (mainWindow != null)
		{
			var result = await dialog.ShowDialog<bool?>(mainWindow);
			return result == true;
		}
		return false;
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

	private static global::Avalonia.Controls.Window? GetMainWindow()
	{
		if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
		{
			return desktop.MainWindow;
		}
		return null;
	}
}
