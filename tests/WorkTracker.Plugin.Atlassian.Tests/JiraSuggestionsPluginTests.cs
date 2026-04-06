using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Atlassian;

namespace WorkTracker.Plugin.Atlassian.Tests;

public class JiraSuggestionsPluginTests : IAsyncDisposable
{
	private const string DefaultFilter = "assignee = currentUser() AND status != Done ORDER BY updated DESC";

	private static readonly Dictionary<string, string> ValidConfig = new()
	{
		["JiraBaseUrl"] = "https://test.atlassian.net",
		["JiraEmail"] = "user@example.com",
		["JiraApiToken"] = "token"
	};

	private readonly MockHttpHandler _httpHandler = new();
	private readonly JiraSuggestionsPlugin _plugin;

	public JiraSuggestionsPluginTests()
	{
		_plugin = new JiraSuggestionsPlugin(
			new MockHttpClientFactory(_httpHandler),
			NullLogger<JiraSuggestionsPlugin>.Instance);
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
		_httpHandler.Dispose();
	}

	private async Task InitializePluginAsync(Dictionary<string, string>? config = null)
	{
		var initialized = await _plugin.InitializeAsync(config ?? ValidConfig, TestContext.Current.CancellationToken);
		initialized.Should().BeTrue("plugin initialization should succeed for the test configuration");
	}

	#region BuildSearchJql — basic composition

	[Fact]
	public void BuildSearchJql_CombinesBaseFilterWithTextSearch()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("fix", DefaultFilter);

		jql.Should().Contain("(assignee = currentUser() AND status != Done)");
		jql.Should().Contain("AND (key ~ \"fix*\"");
		jql.Should().Contain("summary ~ \"fix*\"");
		jql.Should().Contain("text ~ \"fix*\"");
		jql.Should().EndWith("ORDER BY updated DESC");
	}

	[Fact]
	public void BuildSearchJql_FilterWithoutOrderBy_AddsDefaultOrder()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test", "project = PROJ");

		jql.Should().Contain("(project = PROJ) AND");
		jql.Should().EndWith("ORDER BY updated DESC");
	}

	[Fact]
	public void BuildSearchJql_EmptyFilterWithOrderBy_OnlyTextFilterAndOrder()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test", "ORDER BY created ASC");

		jql.Should().StartWith("(key ~ \"test*\"");
		jql.Should().EndWith("ORDER BY created ASC");
		jql.Should().NotContain("() AND");
	}

	[Fact]
	public void BuildSearchJql_EmptyFilter_OnlyTextFilterWithDefaultOrder()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test", "");

		jql.Should().StartWith("(key ~ \"test*\"");
		jql.Should().EndWith("ORDER BY updated DESC");
	}

	#endregion

	#region BuildSearchJql — ORDER BY handling

	[Fact]
	public void BuildSearchJql_PreservesCustomOrderBy()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("bug", "status = Open ORDER BY priority DESC");

		jql.Should().Contain("(status = Open) AND");
		jql.Should().EndWith("ORDER BY priority DESC");
	}

	[Fact]
	public void BuildSearchJql_CaseInsensitiveOrderBy()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("bug", "status = Open order by updated desc");

		jql.Should().Contain("(status = Open) AND");
		jql.Should().EndWith("order by updated desc");
	}

	#endregion

	#region BuildSearchJql — escaping

	[Fact]
	public void BuildSearchJql_EscapesQuotesInQuery()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test\"value", DefaultFilter);

		jql.Should().Contain("test\\\"value*\"");
	}

	[Fact]
	public void BuildSearchJql_EscapesBackslashBeforeQuote()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test\\\"end", DefaultFilter);

		// \ → \\, then " → \" → result: test\\\\\\\"end
		jql.Should().NotContain("test\\\"end*\"");
	}

	[Fact]
	public void BuildSearchJql_StripsNewlines()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("test\r\nvalue", DefaultFilter);

		jql.Should().Contain("testvalue*\"");
		jql.Should().NotContain("\n");
		jql.Should().NotContain("\r");
	}

	[Fact]
	public void BuildSearchJql_StripsAsterisksFromQuery()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("PROJ*-123", DefaultFilter);

		jql.Should().Contain("PROJ-123*\"");
		jql.Should().NotContain("PROJ**");
	}

	[Fact]
	public void BuildSearchJql_TicketIdSearch()
	{
		var jql = JiraSuggestionsPlugin.BuildSearchJql("PROJ-123", "assignee = currentUser()");

		jql.Should().Contain("key ~ \"PROJ-123*\"");
		jql.Should().Contain("(assignee = currentUser()) AND");
	}

	#endregion

	#region EscapeJqlQueryText

	[Fact]
	public void EscapeJqlQueryText_EscapesAllSpecialCharacters()
	{
		var result = JiraSuggestionsPlugin.EscapeJqlQueryText("test\\\"value*\r\n");

		result.Should().Be("test\\\\\\\"value");
	}

	#endregion

	#region ApplySearchTemplate

	[Fact]
	public void ApplySearchTemplate_ReplacesQueryPlaceholder()
	{
		var jql = JiraSuggestionsPlugin.ApplySearchTemplate("fix",
			"project = PROJ AND summary ~ \"{query}*\" ORDER BY updated DESC");

		jql.Should().Be("project = PROJ AND summary ~ \"fix*\" ORDER BY updated DESC");
	}

	[Fact]
	public void ApplySearchTemplate_EscapesSpecialCharacters()
	{
		var jql = JiraSuggestionsPlugin.ApplySearchTemplate("test\"value",
			"summary ~ \"{query}*\"");

		jql.Should().Be("summary ~ \"test\\\"value*\"");
	}

	[Fact]
	public void ApplySearchTemplate_MultipleQueryPlaceholders()
	{
		var jql = JiraSuggestionsPlugin.ApplySearchTemplate("bug",
			"key ~ \"{query}*\" OR summary ~ \"{query}*\"");

		jql.Should().Be("key ~ \"bug*\" OR summary ~ \"bug*\"");
	}

	[Fact]
	public void ApplySearchTemplate_PreservesTemplateStructure()
	{
		var template = "project = PROJ AND (summary ~ \"{query}*\" OR key ~ \"{query}*\") ORDER BY priority DESC";
		var jql = JiraSuggestionsPlugin.ApplySearchTemplate("task", template);

		jql.Should().Be("project = PROJ AND (summary ~ \"task*\" OR key ~ \"task*\") ORDER BY priority DESC");
	}

	#endregion

	#region Integration tests

	[Fact]
	public async Task GetSuggestionsAsync_ReturnsJiraIssues()
	{
		_httpHandler.SetupGetPrefix("/rest/api/3/search/jql",
			"""{"issues": [{"key": "PROJ-1", "fields": {"summary": "Test Issue"}}]}""");
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(DateTime.Today, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		var suggestions = result.Value!;
		suggestions.Should().HaveCount(1);
		suggestions[0].Title.Should().Be("Test Issue");
		suggestions[0].TicketId.Should().Be("PROJ-1");
		suggestions[0].Source.Should().Be("Jira");
	}

	[Fact]
	public async Task GetSuggestionsAsync_HttpError_ReturnsFailure()
	{
		_httpHandler.SetupGetPrefix("/rest/api/3/search/jql", HttpStatusCode.InternalServerError, "Server error");
		await InitializePluginAsync();

		var result = await _plugin.GetSuggestionsAsync(DateTime.Today, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	[Fact]
	public async Task GetSuggestionsAsync_NotInitialized_Throws()
	{
		var act = () => _plugin.GetSuggestionsAsync(DateTime.Today, TestContext.Current.CancellationToken);

		await act.Should().ThrowAsync<InvalidOperationException>();
	}

	[Fact]
	public async Task SearchAsync_ReturnsResults()
	{
		var config = new Dictionary<string, string>(ValidConfig)
		{
			["SearchJqlFilter"] = "project = PROJ AND summary ~ \"{query}*\" ORDER BY updated DESC"
		};
		_httpHandler.SetupGetPrefix("/rest/api/3/search/jql",
			"""{"issues": [{"key": "PROJ-42", "fields": {"summary": "Test task"}}]}""");
		await InitializePluginAsync(config);

		var result = await _plugin.SearchAsync("test", TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
		result.Value.Should().HaveCount(1);
		result.Value![0].TicketId.Should().Be("PROJ-42");
	}

	[Fact]
	public async Task SupportsSearch_WithSearchFilter_ReturnsTrue()
	{
		var config = new Dictionary<string, string>(ValidConfig)
		{
			["SearchJqlFilter"] = "project = PROJ AND summary ~ \"{query}*\""
		};
		await InitializePluginAsync(config);

		_plugin.SupportsSearch.Should().BeTrue();
	}

	[Fact]
	public async Task SupportsSearch_WithoutSearchFilter_ReturnsTrue()
	{
		await InitializePluginAsync();

		_plugin.SupportsSearch.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_Success_ReturnsSuccess()
	{
		_httpHandler.SetupGet("/rest/api/3/myself", """{"displayName":"User"}""");
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsSuccess.Should().BeTrue();
	}

	[Fact]
	public async Task TestConnectionAsync_HttpError_ReturnsFailure()
	{
		_httpHandler.SetupGet("/rest/api/3/myself", HttpStatusCode.Unauthorized, "Unauthorized");
		await InitializePluginAsync();

		var result = await _plugin.TestConnectionAsync(null, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	[Fact]
	public void Metadata_HasCorrectId()
	{
		_plugin.Metadata.Id.Should().Be("jira.suggestions");
	}

	#endregion
}
