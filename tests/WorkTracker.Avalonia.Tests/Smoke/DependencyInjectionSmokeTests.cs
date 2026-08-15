using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.Avalonia.Tests.Smoke;

public class DependencyInjectionSmokeTests
{
	/// <summary>
	/// Builds the exact service registrations the app uses at runtime and instantiates every
	/// registered service. Catches constructor-time failures (missing registrations, broken
	/// dependencies, TypeLoadException from incompatible packages) that otherwise only surface
	/// when the app starts.
	/// </summary>
	[Fact]
	public Task AllRegisteredServices_CanBeConstructed() => UiThread.Dispatch(async () =>
	{
		var dbPath = Path.Combine(Path.GetTempPath(), $"worktracker-di-smoke-{Guid.NewGuid():N}.db");
		var configuration = new ConfigurationBuilder()
			.AddInMemoryCollection(new Dictionary<string, string?>
			{
				["Database:Path"] = dbPath,
				["Database:Pooling"] = "false",
			})
			.Build();

		var services = new ServiceCollection();
		// Logging and IConfiguration are normally contributed by Host.CreateDefaultBuilder
		services.AddLogging();
		services.AddSingleton<IConfiguration>(configuration);
		App.ConfigureAppServices(services, configuration, new LocalizationService());

		try
		{
			// Some registered services implement only IAsyncDisposable, so the container
			// and scope must be disposed asynchronously.
			await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
			{
				ValidateOnBuild = true,
				ValidateScopes = true,
			});

			await using var scope = provider.CreateAsyncScope();
			foreach (var serviceType in services
				.Select(descriptor => descriptor.ServiceType)
				.Where(type => !type.IsGenericTypeDefinition)
				.Distinct())
			{
				scope.ServiceProvider.GetServices(serviceType);
			}
		}
		finally
		{
			try
			{
				File.Delete(dbPath);
			}
			catch (IOException)
			{
				// Best effort — a background initialization task may still hold the file briefly.
			}
		}
	});
}
