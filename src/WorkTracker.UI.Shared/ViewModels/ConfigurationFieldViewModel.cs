using CommunityToolkit.Mvvm.ComponentModel;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.UI.Shared.ViewModels;

/// <summary>
/// ViewModel for a configuration field
/// </summary>
public class ConfigurationFieldViewModel : ObservableObject
{
	private readonly PluginConfigurationField _field;
	private readonly PluginViewModel _pluginViewModel;

	public ConfigurationFieldViewModel(PluginConfigurationField field, PluginViewModel pluginViewModel)
	{
		_field = field;
		_pluginViewModel = pluginViewModel;
	}

	public string Key => _field.Key;
	public string Label => _field.Label;
	public string? Description => _field.Description;
	public string? Placeholder => _field.Placeholder;
	public PluginConfigurationFieldType Type => _field.Type;
	public bool IsRequired => _field.IsRequired;

	public bool IsCheckbox => Type == PluginConfigurationFieldType.Checkbox;
	public bool IsTextInput => !IsCheckbox;

	public string Value
	{
		get => _pluginViewModel.Configuration.TryGetValue(Key, out var value) ? value : string.Empty;
		set
		{
			if (_pluginViewModel.Configuration.TryGetValue(Key, out var current) && current == value)
			{
				return;
			}

			_pluginViewModel.Configuration[Key] = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(BoolValue));
		}
	}

	public bool BoolValue
	{
		get => bool.TryParse(Value, out var parsed) && parsed;
		set
		{
			var serialized = value ? "true" : "false";
			if (Value == serialized)
			{
				return;
			}

			Value = serialized;
		}
	}

	public void RefreshValue()
	{
		OnPropertyChanged(nameof(Value));
		OnPropertyChanged(nameof(BoolValue));
	}
}