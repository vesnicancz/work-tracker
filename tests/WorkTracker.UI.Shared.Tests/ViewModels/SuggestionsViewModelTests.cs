using FluentAssertions;
using Moq;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class SuggestionsViewModelTests
{
	private readonly Mock<IWorkSuggestionOrchestrator> _orchestrator = new();
	private readonly Mock<IWorkSuggestionCache> _cache = new();
	private readonly FakeViewState _viewState = new();

	public SuggestionsViewModelTests()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([]);
	}

	private SuggestionsViewModel CreateViewModel()
	{
		return new SuggestionsViewModel(_orchestrator.Object, _cache.Object, _viewState, TimeProvider.System);
	}

	[Fact]
	public async Task InitializeAsync_DoesNotInvalidateCache()
	{
		using var vm = CreateViewModel();

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

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));
		sequence.Clear();

		await vm.RefreshCommand.ExecuteAsync(null);

		sequence.Should().Equal("invalidate", "load");
	}

	[Fact]
	public async Task ToggleGroupCommand_SingleGroup_KeepsItExpanded()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("only")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));
		var only = vm.Groups.Single();
		only.IsExpanded.Should().BeTrue();

		vm.ToggleGroupCommand.Execute(only);

		only.IsExpanded.Should().BeTrue();
	}

	[Fact]
	public async Task ToggleGroupCommand_CollapsingExpanded_AutoExpandsAnotherGroup()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("first"), CreateGroup("second")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));
		var first = vm.Groups[0];
		var second = vm.Groups[1];
		first.IsExpanded.Should().BeTrue();
		second.IsExpanded.Should().BeFalse();

		vm.ToggleGroupCommand.Execute(first);

		first.IsExpanded.Should().BeFalse();
		second.IsExpanded.Should().BeTrue();
	}

	[Fact]
	public async Task ToggleGroupCommand_ExpandingCollapsed_CollapsesOthers()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("first"), CreateGroup("second")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));
		var first = vm.Groups[0];
		var second = vm.Groups[1];

		vm.ToggleGroupCommand.Execute(second);

		first.IsExpanded.Should().BeFalse();
		second.IsExpanded.Should().BeTrue();
	}

	[Fact]
	public async Task ToggleGroupCommand_StoresExpandedPluginInViewState()
	{
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("first"), CreateGroup("second")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));

		vm.ToggleGroupCommand.Execute(vm.Groups[1]);

		_viewState.LastExpandedPluginId.Should().Be("second");
	}

	[Fact]
	public async Task InitializeAsync_RestoresExpandedGroupFromViewState()
	{
		_viewState.LastExpandedPluginId = "second";
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("first"), CreateGroup("second")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));

		vm.Groups[0].IsExpanded.Should().BeFalse();
		vm.Groups[1].IsExpanded.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_FallsBackToFirstGroup_WhenRememberedPluginNoLongerExists()
	{
		_viewState.LastExpandedPluginId = "removed-plugin";
		_orchestrator.Setup(o => o.GetGroupedSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync([CreateGroup("first"), CreateGroup("second")]);

		using var vm = CreateViewModel();
		await vm.InitializeAsync(new DateTime(2026, 4, 17));

		vm.Groups[0].IsExpanded.Should().BeTrue();
		vm.Groups[1].IsExpanded.Should().BeFalse();
		_viewState.LastExpandedPluginId.Should().Be("first");
	}

	private static SuggestionGroup CreateGroup(string id) => new()
	{
		PluginId = id,
		PluginName = id,
		Items = [],
	};

	private sealed class FakeViewState : ISuggestionsViewState
	{
		public string? LastExpandedPluginId { get; set; }
	}
}
