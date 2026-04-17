using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Helpers;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class CachedWorkSuggestionOrchestratorTests
{
	private readonly Mock<IPluginManager> _mockPluginManager;
	private readonly WorkSuggestionOrchestrator _inner;
	private readonly TestTimeProvider _time;
	private readonly CachedWorkSuggestionOrchestrator _cached;

	public CachedWorkSuggestionOrchestratorTests()
	{
		_mockPluginManager = new Mock<IPluginManager>();
		_inner = new WorkSuggestionOrchestrator(
			_mockPluginManager.Object,
			new Mock<ILogger<WorkSuggestionOrchestrator>>().Object);
		_time = new TestTimeProvider(DateTimeOffset.UtcNow);
		_cached = new CachedWorkSuggestionOrchestrator(_inner, _time);
	}

	private Mock<IWorkSuggestionPlugin> SetupPlugin(
		string id = "jira",
		Func<IReadOnlyList<WorkSuggestion>>? items = null)
	{
		var plugin = MockPluginFactory.CreateSuggestionPlugin(id, id);
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(() => PluginResult<IReadOnlyList<WorkSuggestion>>.Success(
				items?.Invoke() ?? new List<WorkSuggestion>
				{
					new() { Title = "Item", Source = id, SourceId = "1" }
				}));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>(id)).Returns(plugin.Object);
		return plugin;
	}

	[Fact]
	public void HasSuggestionPlugins_DelegatesToInner()
	{
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([]);
		_cached.HasSuggestionPlugins.Should().BeFalse();

		var plugin = MockPluginFactory.CreateSuggestionPlugin();
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);
		_cached.HasSuggestionPlugins.Should().BeTrue();
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_SecondCallSameDate_HitsCache()
	{
		var plugin = SetupPlugin();
		var date = new DateTime(2026, 4, 17);

		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);
		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_AfterTtl_RefetchesFromInner()
	{
		var plugin = SetupPlugin();
		var date = new DateTime(2026, 4, 17);

		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);
		_time.Advance(TimeSpan.FromMinutes(5).Add(TimeSpan.FromSeconds(1)));
		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_DifferentDates_AreCachedSeparately()
	{
		var plugin = SetupPlugin();

		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 17), CancellationToken.None);
		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 18), CancellationToken.None);
		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 17), CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_TimeOfDayIgnored_SameDayHitsCache()
	{
		var plugin = SetupPlugin();

		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 17, 8, 0, 0), CancellationToken.None);
		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 17, 22, 0, 0), CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PassesNormalizedDateToInner()
	{
		var plugin = SetupPlugin();
		var calledWith = new List<DateTime>();
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.Callback<DateTime, CancellationToken>((d, _) => calledWith.Add(d))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>()));

		await _cached.GetGroupedSuggestionsAsync(new DateTime(2026, 4, 17, 14, 30, 0), CancellationToken.None);

		calledWith.Should().ContainSingle().Which.Should().Be(new DateTime(2026, 4, 17));
	}

	[Fact]
	public async Task Invalidate_ForcesRefetchOnNextCall()
	{
		var plugin = SetupPlugin();
		var date = new DateTime(2026, 4, 17);

		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);
		_cached.Invalidate();
		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[Fact]
	public async Task SearchPluginAsync_AlwaysDelegatesNoCache()
	{
		var plugin = MockPluginFactory.CreateSuggestionPlugin("jira", "Jira", supportsSearch: true);
		plugin.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Hit", Source = "Jira", SourceId = "1" }
			}));
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>("jira")).Returns(plugin.Object);

		await _cached.SearchPluginAsync("jira", "fix", new DateTime(2026, 4, 17), CancellationToken.None);
		await _cached.SearchPluginAsync("jira", "fix", new DateTime(2026, 4, 17), CancellationToken.None);

		plugin.Verify(p => p.SearchAsync("fix", It.IsAny<CancellationToken>()), Times.Exactly(2));
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_TwoConcurrentCalls_InnerCalledOnce()
	{
		var plugin = MockPluginFactory.CreateSuggestionPlugin("jira", "Jira");
		var release = new TaskCompletionSource<IReadOnlyList<WorkSuggestion>>(TaskCreationOptions.RunContinuationsAsynchronously);
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.Returns(async () =>
			{
				var items = await release.Task;
				return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(items);
			});
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);
		var date = new DateTime(2026, 4, 17);

		var first = _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);
		var second = _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);

		release.SetResult(new List<WorkSuggestion> { new() { Title = "A", Source = "Jira", SourceId = "1" } });
		await Task.WhenAll(first, second);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_FirstCallerCancels_SecondCallerStillCompletes()
	{
		var plugin = MockPluginFactory.CreateSuggestionPlugin("jira", "Jira");
		var release = new TaskCompletionSource<IReadOnlyList<WorkSuggestion>>(TaskCreationOptions.RunContinuationsAsynchronously);
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.Returns(async (DateTime _, CancellationToken ct) =>
			{
				var items = await release.Task.WaitAsync(ct);
				return PluginResult<IReadOnlyList<WorkSuggestion>>.Success(items);
			});
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);
		var date = new DateTime(2026, 4, 17);

		using var firstCts = new CancellationTokenSource();
		using var secondCts = new CancellationTokenSource();

		var first = _cached.GetGroupedSuggestionsAsync(date, firstCts.Token);
		var second = _cached.GetGroupedSuggestionsAsync(date, secondCts.Token);

		firstCts.Cancel();
		var firstAct = async () => await first;
		await firstAct.Should().ThrowAsync<OperationCanceledException>();

		release.SetResult(new List<WorkSuggestion> { new() { Title = "A", Source = "Jira", SourceId = "1" } });

		var result = await second;
		result.Should().HaveCount(1);
		result[0].Items.Should().HaveCount(1);
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PluginErrorIsCached_NotRetriedWithinTtl()
	{
		// WorkSuggestionOrchestrator catches plugin exceptions and returns Error groups
		// (rather than letting them bubble), so an Error group is treated like any other
		// successful result and cached for the TTL. Manual Refresh is the recovery path.
		var plugin = MockPluginFactory.CreateSuggestionPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("boom"));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);
		var date = new DateTime(2026, 4, 17);

		var firstResult = await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);
		firstResult.Single().Error.Should().NotBeNull();

		await _cached.GetGroupedSuggestionsAsync(date, CancellationToken.None);

		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	private sealed class TestTimeProvider : TimeProvider
	{
		private DateTimeOffset _now;

		public TestTimeProvider(DateTimeOffset start)
		{
			_now = start;
		}

		public override DateTimeOffset GetUtcNow() => _now;

		public void Advance(TimeSpan by)
		{
			_now = _now.Add(by);
		}
	}
}
