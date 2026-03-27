using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for a plugin configuration
/// </summary>
public class PluginViewModel : ObservableObject
{
	private bool _isEnabled = true;

	public IPlugin Plugin { get; }
	public Dictionary<string, string> Configuration { get; } = new();
	public ObservableCollection<ConfigurationFieldViewModel> ConfigurationFields { get; } = new();

	public PluginViewModel(IPlugin plugin)
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
	}

	public string Name => Plugin.Metadata.Name;
	public string Description => Plugin.Metadata.Description ?? string.Empty;
	public string Version => Plugin.Metadata.Version.ToString();
	public string Author => Plugin.Metadata.Author;
	public bool SupportsTestConnection => Plugin is IWorklogUploadPlugin;

	public bool IsEnabled
	{
		get => _isEnabled;
		set => SetProperty(ref _isEnabled, value);
	}
}