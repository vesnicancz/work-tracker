using Microsoft.Extensions.DependencyInjection;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared;

public static class UISharedServiceCollectionExtensions
{
	public static IServiceCollection AddUIShared(this IServiceCollection services)
	{
		services.AddTransient<IWorklogSubmissionOrchestrator, WorklogSubmissionOrchestrator>();
		services.AddTransient<IWorkEntryEditOrchestrator, WorkEntryEditOrchestrator>();
		services.AddTransient<ISettingsOrchestrator, SettingsOrchestrator>();
		services.AddSingleton<ILuxaforService, LuxaforService>();
		services.AddSingleton<IPomodoroService, PomodoroService>();
		return services;
	}
}