using Microsoft.Extensions.DependencyInjection;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared;

public static class UISharedServiceCollectionExtensions
{
	public static IServiceCollection AddUIShared(this IServiceCollection services)
	{
		services.AddTransient<IWorklogSubmissionOrchestrator, WorklogSubmissionOrchestrator>();
		services.AddTransient<IWorkEntryEditOrchestrator, WorkEntryEditOrchestrator>();
		services.AddTransient<ISettingsOrchestrator, SettingsOrchestrator>();
		return services;
	}
}