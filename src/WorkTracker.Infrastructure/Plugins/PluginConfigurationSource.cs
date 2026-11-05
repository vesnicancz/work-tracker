using Microsoft.Extensions.Configuration;
using WorkTracker.Application.Plugins;

namespace WorkTracker.Infrastructure.Plugins;

internal sealed class PluginConfigurationSource(IConfiguration configuration) : IPluginConfigurationSource
{
	private const string SectionName = "Plugins";

	// Lives in the same section, but says where plugins are rather than configuring one
	private const string DirectoriesKey = "Directories";

	public Dictionary<string, Dictionary<string, string>> Resolve(IReadOnlyDictionary<string, Dictionary<string, string>> userConfigurations)
	{
		var configurations = new Dictionary<string, Dictionary<string, string>>();

		foreach (var pluginSection in configuration.GetSection(SectionName).GetChildren())
		{
			if (string.Equals(pluginSection.Key, DirectoriesKey, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			var values = pluginSection.AsEnumerable(makePathsRelative: true)
				.Where(kvp => !string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
				.ToDictionary(kvp => kvp.Key, kvp => kvp.Value!);

			if (values.Count != 0)
			{
				configurations[pluginSection.Key] = values;
			}
		}

		foreach (var (pluginId, userConfiguration) in userConfigurations)
		{
			configurations[pluginId] = userConfiguration;
		}

		return configurations;
	}
}
