using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
		services.AddSingleton<WorkSuggestionOrchestrator>();
		services.AddSingleton<CachedWorkSuggestionOrchestrator>();
		services.AddSingleton<IWorkSuggestionOrchestrator>(sp => sp.GetRequiredService<CachedWorkSuggestionOrchestrator>());
		services.AddSingleton<IWorkSuggestionCache>(sp => sp.GetRequiredService<CachedWorkSuggestionOrchestrator>());
		services.AddSingleton<IPomodoroService, PomodoroService>();
		services.AddSingleton<ISuggestionsViewState, SuggestionsViewState>();

		services.AddSingleton<IUpdateCheckService>(sp =>
		{
			var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
			httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WorkTracker-UpdateCheck/1.0");
			httpClient.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
			return new UpdateCheckService(
				Application.AppInfo.Version,
				httpClient,
				sp.GetRequiredService<ISettingsService>(),
				sp.GetRequiredService<ISystemNotificationService>(),
				sp.GetRequiredService<ILocalizationService>(),
				sp.GetRequiredService<ILogger<UpdateCheckService>>());
		});

		return services;
	}
}