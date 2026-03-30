using System.Reflection;
using System.Runtime.Loader;

namespace WorkTracker.Infrastructure.Plugins;

/// <summary>
/// Custom assembly load context for plugin isolation
/// </summary>
internal class PluginLoadContext : AssemblyLoadContext
{
	private readonly AssemblyDependencyResolver _resolver;

	public PluginLoadContext(string pluginPath)
		: base(isCollectible: true)
	{
		_resolver = new AssemblyDependencyResolver(pluginPath);
	}

	protected override Assembly? Load(AssemblyName assemblyName)
	{
		// If the assembly is already loaded in the default context (shared host assemblies),
		// return null to fall back to it. This prevents duplicate type identities for shared
		// interfaces like IPlugin, which would cause IsAssignableFrom checks to fail.
		foreach (var assembly in Default.Assemblies)
		{
			if (AssemblyName.ReferenceMatchesDefinition(assemblyName, assembly.GetName()))
			{
				return null;
			}
		}

		var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
		if (assemblyPath != null)
		{
			return LoadFromAssemblyPath(assemblyPath);
		}

		return null;
	}

	protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
	{
		var libraryPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
		if (libraryPath != null)
		{
			return LoadUnmanagedDllFromPath(libraryPath);
		}

		return IntPtr.Zero;
	}
}
