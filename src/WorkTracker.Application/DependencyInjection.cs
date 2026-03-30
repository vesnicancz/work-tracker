using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Application.Services;

namespace WorkTracker.Application;

public static class DependencyInjection
{
	public static IServiceCollection AddApplication(this IServiceCollection services)
	{
		// Domain Services - Transient (stateless, pure functions)
		services.AddTransient<IDateRangeService, DateRangeService>();
		services.AddTransient<IWorklogValidator, WorklogValidator>();

		// Application Services - Transient (stateless, uses factory)
		services.AddTransient<IWorkEntryService, WorkEntryService>();

		// Register plugin-based services
		services.AddTransient<IWorklogSubmissionService, PluginBasedWorklogSubmissionService>();

		// Time abstraction - Singleton (allows test substitution)
		services.AddSingleton(TimeProvider.System);

		return services;
	}
}
