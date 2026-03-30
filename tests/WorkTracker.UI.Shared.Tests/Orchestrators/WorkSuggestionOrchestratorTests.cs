using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Orchestrators;

namespace WorkTracker.UI.Shared.Tests.Orchestrators;

public class WorkSuggestionOrchestratorTests
{
	private readonly Mock<IPluginManager> _mockPluginManager;
	private readonly WorkSuggestionOrchestrator _orchestrator;

	public WorkSuggestionOrchestratorTests()
	{
		_mockPluginManager = new Mock<IPluginManager>();
		_orchestrator = new WorkSuggestionOrchestrator(
			_mockPluginManager.Object,
			new Mock<ILogger<WorkSuggestionOrchestrator>>().Object);
	}

	#region HasSuggestionPlugins

	[Fact]
	public void HasSuggestionPlugins_NoPlugins_ReturnsFalse()
	{
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins)
			.Returns(Enumerable.Empty<IWorkSuggestionPlugin>());

		_orchestrator.HasSuggestionPlugins.Should().BeFalse();
	}

	[Fact]
	public void HasSuggestionPlugins_WithPlugins_ReturnsTrue()
	{
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins)
			.Returns([CreateMockPlugin("test", "Test").Object]);

		_orchestrator.HasSuggestionPlugins.Should().BeTrue();
	}

	#endregion

	#region GetGroupedSuggestionsAsync

	[Fact]
	public async Task GetGroupedSuggestionsAsync_NoPlugins_ReturnsEmpty()
	{
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins)
			.Returns(Enumerable.Empty<IWorkSuggestionPlugin>());

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		result.Should().BeEmpty();
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PluginReturnsItems_GroupHasItems()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Fix bug", TicketId = "PROJ-1", Source = "Jira", SourceId = "PROJ-1" },
				new() { Title = "Add feature", TicketId = "PROJ-2", Source = "Jira", SourceId = "PROJ-2" }
			}));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(1);
		result[0].PluginId.Should().Be("jira");
		result[0].Items.Should().HaveCount(2);
		result[0].Error.Should().BeNull();
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PluginReturnsFailure_GroupHasError()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Failure("API error 401"));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(1);
		result[0].Items.Should().BeEmpty();
		result[0].Error.Should().Be("API error 401");
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PluginThrows_GroupHasError()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("Network error"));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(1);
		result[0].Items.Should().BeEmpty();
		result[0].Error.Should().Be("Failed to load suggestions");
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_PluginThrowsOperationCanceled_Rethrows()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new OperationCanceledException());
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);

		var act = () => _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_MultiplePlugins_OneFailsOneSucceeds_BothGroupsReturned()
	{
		var good = CreateMockPlugin("calendar", "Calendar");
		good.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Standup", Source = "Calendar", SourceId = "evt1", StartTime = DateTime.Today.AddHours(9) }
			}));

		var bad = CreateMockPlugin("jira", "Jira");
		bad.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Failure("Auth failed"));

		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([good.Object, bad.Object]);

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(2);
		result.Single(g => g.PluginId == "calendar").Items.Should().HaveCount(1);
		result.Single(g => g.PluginId == "jira").Error.Should().Be("Auth failed");
	}

	[Fact]
	public async Task GetGroupedSuggestionsAsync_ItemsSortedByStartTimeThenTitle()
	{
		var plugin = CreateMockPlugin("cal", "Calendar");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Lunch", Source = "Cal", SourceId = "3", StartTime = DateTime.Today.AddHours(12) },
				new() { Title = "Standup", Source = "Cal", SourceId = "1", StartTime = DateTime.Today.AddHours(9) },
				new() { Title = "No time B", Source = "Cal", SourceId = "5" },
				new() { Title = "No time A", Source = "Cal", SourceId = "4" },
			}));
		_mockPluginManager.Setup(m => m.WorkSuggestionPlugins).Returns([plugin.Object]);

		var result = await _orchestrator.GetGroupedSuggestionsAsync(DateTime.Today, CancellationToken.None);

		var titles = result[0].Items.Select(i => i.Title).ToList();
		titles.Should().Equal("Standup", "Lunch", "No time A", "No time B");
	}

	#endregion

	#region SearchPluginAsync

	[Fact]
	public async Task SearchPluginAsync_PluginNotFound_ReturnsEmpty()
	{
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>(It.IsAny<string>()))
			.Returns((IWorkSuggestionPlugin?)null);

		var result = await _orchestrator.SearchPluginAsync("unknown", "test", DateTime.Today, CancellationToken.None);

		result.Should().BeEmpty();
	}

	[Fact]
	public async Task SearchPluginAsync_EmptyQuery_CallsGetSuggestions()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Default item", Source = "Jira", SourceId = "1" }
			}));
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>("jira")).Returns(plugin.Object);

		var result = await _orchestrator.SearchPluginAsync("jira", "", DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(1);
		result[0].Title.Should().Be("Default item");
		plugin.Verify(p => p.GetSuggestionsAsync(It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
		plugin.Verify(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task SearchPluginAsync_NonEmptyQuery_CallsSearch()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.SupportsSearch).Returns(true);
		plugin.Setup(p => p.SearchAsync("fix", It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<IReadOnlyList<WorkSuggestion>>.Success(new List<WorkSuggestion>
			{
				new() { Title = "Fix bug", TicketId = "PROJ-1", Source = "Jira", SourceId = "PROJ-1" }
			}));
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>("jira")).Returns(plugin.Object);

		var result = await _orchestrator.SearchPluginAsync("jira", "fix", DateTime.Today, CancellationToken.None);

		result.Should().HaveCount(1);
		plugin.Verify(p => p.SearchAsync("fix", It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task SearchPluginAsync_PluginThrows_ReturnsEmpty()
	{
		var plugin = CreateMockPlugin("jira", "Jira");
		plugin.Setup(p => p.SupportsSearch).Returns(true);
		plugin.Setup(p => p.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new HttpRequestException("Timeout"));
		_mockPluginManager.Setup(m => m.GetPlugin<IWorkSuggestionPlugin>("jira")).Returns(plugin.Object);

		var result = await _orchestrator.SearchPluginAsync("jira", "test", DateTime.Today, CancellationToken.None);

		result.Should().BeEmpty();
	}

	#endregion

	private static Mock<IWorkSuggestionPlugin> CreateMockPlugin(string id, string name)
	{
		var mock = new Mock<IWorkSuggestionPlugin>();
		mock.Setup(p => p.Metadata).Returns(new PluginMetadata
		{
			Id = id,
			Name = name,
			Version = new Version(1, 0),
			Author = "Test"
		});
		mock.Setup(p => p.SupportsSearch).Returns(false);
		return mock;
	}
}
