using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.StreamDeck.Tests;

/// <summary>
/// The plugin and the Stream Deck part are written in two languages, and these files are what they
/// share: protocol-fixtures.test.ts checks that the client reads and produces the same ones.
/// </summary>
public class RemoteControlProtocolFixtureTests
{
	private static readonly DateTimeOffset TenOClock = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(1));

	private static string Fixture(string name)
	{
		var directory = new DirectoryInfo(AppContext.BaseDirectory);
		while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WorkTracker.slnx")))
		{
			directory = directory.Parent;
		}

		return File.ReadAllText(Path.Combine(directory!.FullName, "streamdeck-plugin", "test", "fixtures", "protocol", $"{name}.json"));
	}

	private static void ShouldMatchFixture<T>(T message, string fixture)
	{
		var produced = JsonSerializer.SerializeToNode(message, RemoteControlProtocol.JsonOptions);
		var expected = JsonNode.Parse(Fixture(fixture));

		JsonNode.DeepEquals(produced, expected).Should().BeTrue($"the message should match {fixture}.json, but was {produced!.ToJsonString()}");
	}

	[Fact]
	public void State_MatchesTheFixture()
	{
		var state = new RemoteControlState(
			new RemoteWorkItem("PROJ-1", "Coding", new DateTimeOffset(2026, 11, 4, 9, 30, 0, TimeSpan.FromHours(1))),
			null,
			new RemotePomodoroState(StatusIndicatorState.ShortBreak, TimeSpan.FromSeconds(90), 2, 4));

		ShouldMatchFixture(RemoteStateMessage.From(state), "state");
	}

	[Fact]
	public void SuggestionsResult_MatchesTheFixture()
	{
		var suggestions = new RemoteSuggestions(
			[
				new RemoteSuggestion("Standup", null, TenOClock, TenOClock.AddMinutes(15), "O365 Calendar"),
				new RemoteSuggestion("Fix login", "PROJ-1", null, null, "Jira")
			],
			["GitHub"]);

		ShouldMatchFixture(new RemoteResponse(3, true, null, suggestions), "suggestions-result");
	}

	[Fact]
	public void FavoritesResult_MatchesTheFixture()
	{
		var favorites = new[]
		{
			new RemoteFavorite("Support", "PROJ-2", null, false),
			new RemoteFavorite("Meeting", null, "Weekly", true)
		};

		ShouldMatchFixture(new RemoteResponse(4, true, null, Favorites: favorites), "favorites-result");
	}

	[Fact]
	public void AddWorkRequest_IsReadFromTheFixture()
	{
		var request = JsonSerializer.Deserialize<RemoteRequest>(Fixture("add-work-request"), RemoteControlProtocol.JsonOptions)!;

		request.Id.Should().Be(1);
		request.Command.Should().Be(RemoteControlProtocol.Commands.AddWork);
		request.TicketId.Should().Be("PROJ-1");
		request.Description.Should().Be("Standup");
		request.StartTime.Should().Be(TenOClock);
		request.EndTime.Should().Be(TenOClock.AddMinutes(15));
	}
}
