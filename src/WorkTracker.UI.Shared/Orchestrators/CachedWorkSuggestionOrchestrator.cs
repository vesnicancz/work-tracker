using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Orchestrators;

/// <summary>
/// Caches grouped suggestions per date for a short TTL so reopening the dialog
/// does not refetch from every plugin. Search results are not cached.
/// </summary>
public class CachedWorkSuggestionOrchestrator : IWorkSuggestionOrchestrator, IWorkSuggestionCache
{
	private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

	private readonly WorkSuggestionOrchestrator _inner;
	private readonly TimeProvider _timeProvider;
	private readonly Dictionary<DateTime, CacheEntry> _entries = new();
	private readonly object _lock = new();

	public CachedWorkSuggestionOrchestrator(WorkSuggestionOrchestrator inner, TimeProvider timeProvider)
	{
		_inner = inner;
		_timeProvider = timeProvider;
	}

	public bool HasSuggestionPlugins => _inner.HasSuggestionPlugins;

	public Task<IReadOnlyList<SuggestionGroup>> GetGroupedSuggestionsAsync(DateTime date, CancellationToken cancellationToken)
	{
		var key = date.Date;
		Task<IReadOnlyList<SuggestionGroup>> task;

		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var existing) && !IsExpired(existing))
			{
				task = existing.Task;
			}
			else
			{
				// Detach the cached Task from any single caller's token: if the first caller
				// closes their dialog mid-flight, the shared Task must keep running for any
				// concurrent caller (and for a future caller that hits the cache).
				task = _inner.GetGroupedSuggestionsAsync(date, CancellationToken.None);
				_entries[key] = new CacheEntry(task, _timeProvider.GetUtcNow());
			}
		}

		return AwaitAndReleaseOnFailure(key, task, cancellationToken);
	}

	public Task<IReadOnlyList<WorkSuggestionViewModel>> SearchPluginAsync(
		string pluginId, string query, DateTime date, CancellationToken cancellationToken)
	{
		return _inner.SearchPluginAsync(pluginId, query, date, cancellationToken);
	}

	public void Invalidate()
	{
		lock (_lock)
		{
			_entries.Clear();
		}
	}

	private async Task<IReadOnlyList<SuggestionGroup>> AwaitAndReleaseOnFailure(
		DateTime key, Task<IReadOnlyList<SuggestionGroup>> task, CancellationToken cancellationToken)
	{
		try
		{
			return await task.WaitAsync(cancellationToken);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out var entry) && entry.Task == task)
				{
					_entries.Remove(key);
				}
			}
			throw;
		}
	}

	private bool IsExpired(CacheEntry entry)
	{
		return _timeProvider.GetUtcNow() - entry.CachedAt >= CacheTtl;
	}

	private sealed record CacheEntry(Task<IReadOnlyList<SuggestionGroup>> Task, DateTimeOffset CachedAt);
}
