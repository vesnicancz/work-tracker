using FluentAssertions;
using Moq;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class SuggestionsViewModelTests
{
	private readonly Mock<IWorkSuggestionOrchestrator> _orchestrator = new();
	private readonly Mock<IWorkSuggestionCache> _cache = new();

	public SuggestionsViewModelTests()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
	}

	[Fact]
	public async Task InitializeAsync_DoesNotInvalidateCache()
	{
		using var vm = new SuggestionsViewModel(_orchestrator.Object, _cache.Object);

		await vm.InitializeAsync(new DateTime(2026, 4, 17));

		_cache.Verify(c => c.Invalidate(), Times.Never);
		_orchestrator.Verify(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task RefreshCommand_InvalidatesCacheBeforeReload()
	{
		var sequence = new List<string>();
		_cache.Setup(c => c.Invalidate()).Callback(() => sequence.Add("invalidate"));
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(() =>
			{
				sequence.Add("load");
				return [];
			});

		using var vm = new SuggestionsViewModel(_orchestrator.Object, _cache.Object);
		await vm.InitializeAsync(new DateTime(2026, 4, 17));
		sequence.Clear();

		await vm.RefreshCommand.ExecuteAsync(null);

		sequence.Should().Equal("invalidate", "load");
	}
}
