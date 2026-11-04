using Microsoft.Extensions.DependencyInjection;
using WorkTracker.Application.Common;
using WorkTracker.Application.Services;
using WorkTracker.Domain.Entities;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.Services;

/// <summary>
/// Gives remote control plugins access to the application. Commands run on the UI thread: the services
/// raise their events on the caller's thread, and view models subscribed to them touch UI objects.
/// </summary>
public sealed class RemoteControlHost : IRemoteControlHost
{
	private readonly IWorklogStateService _worklogStateService;
	private readonly IPomodoroService _pomodoroService;
	private readonly ISettingsService _settingsService;
	private readonly IWorkSuggestionOrchestrator _suggestionOrchestrator;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUiDispatcher _uiDispatcher;
	private readonly TimeProvider _timeProvider;

	public RemoteControlHost(
		IWorklogStateService worklogStateService,
		IPomodoroService pomodoroService,
		ISettingsService settingsService,
		IWorkSuggestionOrchestrator suggestionOrchestrator,
		IServiceScopeFactory scopeFactory,
		IUiDispatcher uiDispatcher,
		TimeProvider timeProvider)
	{
		_worklogStateService = worklogStateService;
		_pomodoroService = pomodoroService;
		_settingsService = settingsService;
		_suggestionOrchestrator = suggestionOrchestrator;
		_scopeFactory = scopeFactory;
		_uiDispatcher = uiDispatcher;
		_timeProvider = timeProvider;

		_worklogStateService.ActiveWorkChanged += (_, _) => RaiseStateChanged();
		_worklogStateService.WorkEntriesModified += (_, _) => RaiseStateChanged();
		_pomodoroService.PhaseChanged += (_, _) => RaiseStateChanged();
	}

	public event EventHandler? StateChanged;

	public async Task<RemoteControlState> GetStateAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken)
	{
		var activeWork = _worklogStateService.ActiveWork;
		var lastWork = activeWork == null ? await FindLastWorkAsync(lastWorkMaxAge, cancellationToken) : null;
		var pomodoro = _pomodoroService.GetSnapshot();

		return new RemoteControlState(
			ToRemoteWorkItem(activeWork),
			ToRemoteWorkItem(lastWork),
			new RemotePomodoroState(
				ToIndicatorState(pomodoro.CurrentPhase),
				pomodoro.TimeRemaining,
				pomodoro.CompletedPomodoros,
				pomodoro.PomodorosBeforeLongBreak));
	}

	public Task<PluginResult> StartWorkAsync(string? ticketId, string? description, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(async () => ToPluginResult(
			await _worklogStateService.StartTrackingAsync(ticketId, description, cancellationToken)), cancellationToken);
	}

	public Task<PluginResult> StopWorkAsync(CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(async () => ToPluginResult(
			await _worklogStateService.StopTrackingAsync(cancellationToken)), cancellationToken);
	}

	public async Task<PluginResult> ResumeLastWorkAsync(TimeSpan lastWorkMaxAge, CancellationToken cancellationToken)
	{
		var lastWork = await FindLastWorkAsync(lastWorkMaxAge, cancellationToken);
		if (lastWork == null)
		{
			return PluginResult.Failure("There is no recent work to resume.", PluginErrorCategory.NotFound);
		}

		return await StartWorkAsync(lastWork.TicketId, lastWork.Description, cancellationToken);
	}

	public Task<PluginResult> ShiftActiveWorkStartAsync(TimeSpan delta, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(async () =>
		{
			var activeWork = _worklogStateService.ActiveWork;
			if (activeWork == null)
			{
				return PluginResult.Failure("No work is being tracked.", PluginErrorCategory.NotFound);
			}

			var newStart = activeWork.StartTime + delta;
			if (newStart > _timeProvider.GetLocalNow().DateTime)
			{
				return PluginResult.Failure("Work cannot start in the future.", PluginErrorCategory.Validation);
			}

			return ToPluginResult(await _worklogStateService.UpdateWorkEntryAsync(
				activeWork.Id, activeWork.TicketId, newStart, null, activeWork.Description, cancellationToken));
		}, cancellationToken);
	}

	public Task<PluginResult> AddWorkEntryAsync(string? ticketId, string? description, DateTimeOffset startTime, DateTimeOffset? endTime, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(async () =>
		{
			var start = ToLocalTime(startTime);
			DateTime? end = endTime == null ? null : ToLocalTime(endTime.Value);

			var plan = await _worklogStateService.ComputeOverlapResolutionAsync(null, start, end, cancellationToken);
			if (plan.HasAdjustments && !plan.IsOnlyClosingActiveEntry)
			{
				return PluginResult.Failure("The entry would change other entries. Add it in the application.", PluginErrorCategory.Validation);
			}

			return plan.HasAdjustments
				? ToPluginResult(await _worklogStateService.CreateWorkEntryWithResolutionAsync(ticketId, start, description, end, plan, cancellationToken))
				: ToPluginResult(await _worklogStateService.CreateWorkEntryAsync(ticketId, start, description, end, cancellationToken));
		}, cancellationToken);
	}

	public async Task<RemoteSuggestions> GetSuggestionsAsync(CancellationToken cancellationToken)
	{
		var today = _timeProvider.GetLocalNow().Date;
		var groups = await _suggestionOrchestrator.GetGroupedSuggestionsAsync(today, cancellationToken);

		return new RemoteSuggestions(
			groups.SelectMany(g => g.Items)
				.Select(s => new RemoteSuggestion(s.Title, s.TicketId, ToLocalOffset(s.StartTime), ToLocalOffset(s.EndTime), s.Source))
				.ToList(),
			groups.Where(g => g.Error != null).Select(g => g.PluginName).ToList());
	}

	public Task<PluginResult> StartPomodoroAsync(CancellationToken cancellationToken)
	{
		if (!_settingsService.Settings.Pomodoro.Enabled)
		{
			return Task.FromResult(PluginResult.Failure("Pomodoro is turned off in the settings.", PluginErrorCategory.Validation));
		}

		return OnUiThreadAsync(_pomodoroService.Start, cancellationToken);
	}

	public Task<PluginResult> StopPomodoroAsync(CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(_pomodoroService.Stop, cancellationToken);
	}

	public Task<PluginResult> SkipPomodoroPhaseAsync(CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(_pomodoroService.Skip, cancellationToken);
	}

	private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

	private Task<PluginResult> OnUiThreadAsync(Func<Task<PluginResult>> action, CancellationToken cancellationToken)
	{
		return _uiDispatcher.InvokeAsync(action, cancellationToken);
	}

	private Task<PluginResult> OnUiThreadAsync(Action action, CancellationToken cancellationToken)
	{
		return OnUiThreadAsync(() =>
		{
			action();
			return Task.FromResult(PluginResult.Success());
		}, cancellationToken);
	}

	private async Task<WorkEntry?> FindLastWorkAsync(TimeSpan maxAge, CancellationToken cancellationToken)
	{
		var now = _timeProvider.GetLocalNow().DateTime;
		var oldest = now - maxAge;

		using var scope = _scopeFactory.CreateScope();
		var workEntryService = scope.ServiceProvider.GetRequiredService<IWorkEntryService>();
		var entries = await workEntryService.GetWorkEntriesByDateRangeAsync(oldest, now, cancellationToken);

		// The range covers whole days, so it can return entries from earlier on the oldest day
		return entries.Where(e => e.StartTime >= oldest).MaxBy(e => e.StartTime);
	}

	private RemoteWorkItem? ToRemoteWorkItem(WorkEntry? entry)
	{
		if (entry == null)
		{
			return null;
		}

		return new RemoteWorkItem(entry.TicketId, entry.Description, ToLocalOffset(entry.StartTime));
	}

	private DateTime ToLocalTime(DateTimeOffset time) => TimeZoneInfo.ConvertTime(time, _timeProvider.LocalTimeZone).DateTime;

	private DateTimeOffset? ToLocalOffset(DateTime? time) => time == null ? null : ToLocalOffset(time.Value);

	private DateTimeOffset ToLocalOffset(DateTime time)
	{
		var local = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);
		return new DateTimeOffset(local, _timeProvider.LocalTimeZone.GetUtcOffset(local));
	}

	private static StatusIndicatorState ToIndicatorState(PomodoroPhase phase) => phase switch
	{
		PomodoroPhase.Work => StatusIndicatorState.Work,
		PomodoroPhase.ShortBreak => StatusIndicatorState.ShortBreak,
		PomodoroPhase.LongBreak => StatusIndicatorState.LongBreak,
		_ => StatusIndicatorState.Idle
	};

	private static PluginResult ToPluginResult(Result result)
	{
		return result.IsSuccess ? PluginResult.Success() : PluginResult.Failure(result.Error, PluginErrorCategory.Validation);
	}
}
