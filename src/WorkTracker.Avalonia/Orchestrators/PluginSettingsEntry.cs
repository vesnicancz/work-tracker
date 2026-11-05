using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Avalonia.Orchestrators;

/// <summary>
/// A plugin with its enabled state and configuration, as stored or as the user left them in the dialog.
/// Passwords are in the clear here; they are protected on the way to the settings file.
/// </summary>
public sealed record PluginSettingsEntry(IPluginDescriptor Plugin, bool IsEnabled, IReadOnlyDictionary<string, string> Configuration);
