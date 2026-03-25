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
		// Only worklog upload plugins have configuration fields
		if (plugin is IWorklogUploadPlugin worklogPlugin)
		{
			foreach (var field in worklogPlugin.GetConfigurationFields())
			{
				var value = string.Empty;
				if (!string.IsNullOrEmpty(field.DefaultValue))
				{
					value = field.DefaultValue;
					Configuration[field.Key] = value;
				}

				ConfigurationFields.Add(new ConfigurationFieldViewModel(field, this));
			}
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