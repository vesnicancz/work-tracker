using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WorkTracker.Avalonia.Orchestrators;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// The Plugins tab of the Settings window
/// </summary>
public class PluginSettingsViewModel : ViewModelBase
{
	private readonly ISettingsOrchestrator _orchestrator;
	private readonly ILogger _logger;
	private PluginViewModel? _selectedItem;
	private string? _testConnectionResult;
	private bool _isTestingConnection;

	public PluginSettingsViewModel(ISettingsOrchestrator orchestrator, ILogger logger)
	{
		_orchestrator = orchestrator;
		_logger = logger;

		TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => SelectedItem != null && !IsTestingConnection);

		try
		{
			foreach (var entry in _orchestrator.LoadPlugins())
			{
				var plugin = new PluginViewModel(entry.Plugin) { IsEnabled = entry.IsEnabled };
				plugin.ApplyConfiguration(entry.Configuration);
				plugin.PropertyChanged += OnPluginPropertyChanged;
				Items.Add(plugin);
			}

			if (Items.Any())
			{
				SelectedItem = Items.First();
			}
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to load plugins in SettingsViewModel");
		}
	}

	public ObservableCollection<PluginViewModel> Items { get; } = new();

	public bool HasPlugins => Items.Count > 0;

	public bool HasEnabledRemoteControlPlugin => Items.Any(p => p.IsRemoteControl && p.IsEnabled);

	public PluginViewModel? SelectedItem
	{
		get => _selectedItem;
		set
		{
			if (SetProperty(ref _selectedItem, value))
			{
				TestConnectionResult = null;
				TestConnectionCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public string? TestConnectionResult
	{
		get => _testConnectionResult;
		set => SetProperty(ref _testConnectionResult, value);
	}

	public bool IsTestingConnection
	{
		get => _isTestingConnection;
		set
		{
			if (SetProperty(ref _isTestingConnection, value))
			{
				TestConnectionCommand.NotifyCanExecuteChanged();
			}
		}
	}

	public IAsyncRelayCommand TestConnectionCommand { get; }

	private void OnPluginPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(PluginViewModel.IsEnabled))
		{
			OnPropertyChanged(nameof(HasEnabledRemoteControlPlugin));
		}
	}

	private async Task TestConnectionAsync()
	{
		if (SelectedItem == null)
		{
			return;
		}

		var pluginId = SelectedItem.Plugin.Metadata.Id;
		IsTestingConnection = true;
		TestConnectionResult = null;

		try
		{
			var progress = new Progress<string>(message => TestConnectionResult = message);
			TestConnectionResult = await _orchestrator.TestConnectionAsync(SelectedItem.ToEntry(), progress, CancellationToken.None);
		}
		catch (Exception ex)
		{
			TestConnectionResult = $"✗ Error: {ex.Message}";
			_logger.LogError(ex, "Error testing connection for {PluginId}", pluginId);
		}
		finally
		{
			IsTestingConnection = false;
		}
	}
}
