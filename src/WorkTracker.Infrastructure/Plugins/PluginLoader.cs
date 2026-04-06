using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Plugins;

internal sealed class PluginLoader(IServiceProvider pluginServiceProvider, ILogger<PluginLoader> logger)
{
	public IReadOnlyList<(IPlugin Plugin, AssemblyLoadContext Context)> LoadFromFile(string assemblyPath)
	{
		var results = new List<(IPlugin Plugin, AssemblyLoadContext Context)>();

		var context = new PluginLoadContext(assemblyPath);
		var assembly = context.LoadFromAssemblyName(
			new AssemblyName(Path.GetFileNameWithoutExtension(assemblyPath))
		);

		var pluginTypes = assembly.GetTypes()
			.Where(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
			.ToList();

		if (pluginTypes.Count == 0)
		{
			logger.LogWarning("No plugin types found in {Assembly}", assemblyPath);
			context.Unload();
			return results;
		}

		foreach (var pluginType in pluginTypes)
		{
			try
			{
				var plugin = ActivatorUtilities.CreateInstance(pluginServiceProvider, pluginType) as IPlugin;
				if (plugin == null)
				{
					logger.LogWarning("Failed to create instance of {Type}", pluginType.FullName);
					continue;
				}

				results.Add((plugin, context));
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "Failed to instantiate plugin type {Type}", pluginType.FullName);
			}
		}

		if (results.Count == 0)
		{
			context.Unload();
		}

		return results;
	}

	public T LoadEmbedded<T>() where T : class, IPlugin
	{
		return ActivatorUtilities.CreateInstance<T>(pluginServiceProvider);
	}

	public IReadOnlyList<string> DiscoverPluginFiles(IEnumerable<string> directories)
	{
		var results = new List<string>();

		foreach (var directory in directories)
		{
			var pluginFiles = Directory.GetFiles(directory, "WorkTracker.Plugin.*.dll", SearchOption.AllDirectories);
			results.AddRange(pluginFiles);
		}

		return results;
	}
}
