using FluentAssertions;
using WorkTracker.Plugin.Atlassian;

namespace WorkTracker.Plugin.Atlassian.Tests;

public class JiraSuggestionsPluginTests
{
	private const string DefaultFilter = "assignee = currentUser() AND status != Done ORDER BY updated DESC";

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
}
