using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.ViewModels;

/// <summary>
/// ViewModel for a plugin configuration
/// </summary>
public class PluginViewModel : ObservableObject
{
	private bool _isEnabled;
	private IReadOnlyList<PluginConnectionDetail> _connectionDetails = [];

	public IPluginDescriptor Plugin { get; }
	public Dictionary<string, string> Configuration { get; } = new();
	public ObservableCollection<ConfigurationFieldViewModel> ConfigurationFields { get; } = new();

	public PluginViewModel(IPluginDescriptor plugin)
	{
		Plugin = plugin;

		// Initialize configuration with default values and create field view models
		foreach (var field in plugin.GetConfigurationFields())
		{
			if (!string.IsNullOrEmpty(field.DefaultValue))
			{
				Configuration[field.Key] = field.DefaultValue;
			}

			ConfigurationFields.Add(new ConfigurationFieldViewModel(field, this));
		}

		RefreshConnectionDetails();
	}

	/// <summary>
	/// Puts stored values over the defaults. A key without a field stays in the configuration, so saving
	/// doesn't drop it.
	/// </summary>
	public void ApplyConfiguration(IReadOnlyDictionary<string, string> configuration)
	{
		foreach (var (key, value) in configuration)
		{
			Configuration[key] = value;
		}

		foreach (var field in ConfigurationFields)
		{
			field.RefreshValue();
		}

		RefreshConnectionDetails();
	}

	public void RefreshConnectionDetails()
	{
		if (Plugin is IConnectionDetailsProvider provider)
		{
			ConnectionDetails = provider.GetConnectionDetails(Configuration);
		}
	}

	/// <summary>
	/// What to enter in the program that connects to the plugin, for the configuration as edited.
	/// </summary>
	public IReadOnlyList<PluginConnectionDetail> ConnectionDetails
	{
		get => _connectionDetails;
		private set
		{
			if (!_connectionDetails.SequenceEqual(value))
			{
				_connectionDetails = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(HasConnectionDetails));
			}
		}
	}

	public bool HasConnectionDetails => ConnectionDetails.Count > 0;

	public PluginSettingsEntry ToEntry() => new(Plugin, IsEnabled, new Dictionary<string, string>(Configuration));

	public string Name => Plugin.Metadata.Name;
	public string Description => Plugin.Metadata.Description ?? string.Empty;
	public string Version => Plugin.Metadata.Version.ToString();
	public string Author => Plugin.Metadata.Author;
	public bool SupportsTestConnection => Plugin is ITestablePlugin;
	public bool IsRemoteControl => Plugin is IRemoteControlPlugin;

	public bool IsEnabled
	{
		get => _isEnabled;
		set => SetProperty(ref _isEnabled, value);
	}
}
