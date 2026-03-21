using System.Reflection;

namespace WorkTracker.Application;

public static class AppInfo
{
	public static string Version { get; } =
		Assembly.GetEntryAssembly()?
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion ?? "dev";
}
