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
		TaskCompletionSource<IReadOnlyList<SuggestionGroup>>? owned = null;

		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var existing) && !IsExpired(existing))
			{
				task = existing.Task;
			}
			else
			{
				EvictExpired();
				owned = new TaskCompletionSource<IReadOnlyList<SuggestionGroup>>(TaskCreationOptions.RunContinuationsAsynchronously);
				task = owned.Task;
				_entries[key] = new CacheEntry(task, _timeProvider.GetUtcNow());
			}
		}

		if (owned != null)
		{
			// Run the inner fetch outside the lock so plugin code (sync prefix of each
			// plugin's GetSuggestionsAsync) cannot block other callers entering the cache.
			// CancellationToken.None: the shared Task must outlive any single caller closing
			// their dialog so concurrent callers and the cache slot stay valid.
			_ = FetchAndComplete(key, owned);
		}

		return AwaitAndReleaseOnFailure(key, task, cancellationToken);
	}

	private async Task FetchAndComplete(DateTime key, TaskCompletionSource<IReadOnlyList<SuggestionGroup>> tcs)
	{
		try
		{
			var result = await _inner.GetGroupedSuggestionsAsync(key, CancellationToken.None);
			tcs.SetResult(result);
		}
		catch (Exception ex)
		{
			tcs.SetException(ex);
		}
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
		// In-flight Task is never expired: starting a parallel fetch would defeat single-flight
		// (the awaiting caller would pay the wait twice and the inner orchestrator would do
		// duplicate plugin work). Once completed, TTL counts from CachedAt.
		if (!entry.Task.IsCompleted)
		{
			return false;
		}
		return _timeProvider.GetUtcNow() - entry.CachedAt >= CacheTtl;
	}

	private void EvictExpired()
	{
		List<DateTime>? expiredKeys = null;
		foreach (var (k, v) in _entries)
		{
			if (IsExpired(v))
			{
				expiredKeys ??= new List<DateTime>();
				expiredKeys.Add(k);
			}
		}
		if (expiredKeys != null)
		{
			foreach (var k in expiredKeys)
			{
				_entries.Remove(k);
			}
		}
	}

	private sealed record CacheEntry(Task<IReadOnlyList<SuggestionGroup>> Task, DateTimeOffset CachedAt);
}
