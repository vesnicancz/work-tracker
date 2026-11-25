using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Moq;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.Mcp.Tests;

public sealed class McpPluginTests : IAsyncLifetime
{
	private const string Token = "0123456789abcdef";
	private const string Initialize = """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"test","version":"1"}}}""";
	private const string Initialized = """{"jsonrpc":"2.0","method":"notifications/initialized"}""";

	private readonly int _port = FindFreePort();
	private readonly Mock<IRemoteControlHost> _host = new();
	private readonly HttpClient _http = new();
	private readonly McpPlugin _plugin = new(NullLogger<McpPlugin>.Instance);

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	private Uri Endpoint => new($"http://localhost:{_port}/mcp");

	public ValueTask InitializeAsync() => ValueTask.CompletedTask;

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
		_http.Dispose();
	}

	private static int FindFreePort()
	{
		using var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		return ((IPEndPoint)listener.LocalEndpoint).Port;
	}

	private Dictionary<string, string> Configuration(bool allowSubmission = false) => new()
	{
		["port"] = _port.ToString(),
		["access_token"] = Token,
		["allow_worklog_submission"] = allowSubmission.ToString()
	};

	[Fact]
	public async Task AccessToken_DefaultIsRandomAndValid()
	{
		string GetDefault() => _plugin.GetConfigurationFields().Single(f => f.Key == "access_token").DefaultValue!;
		var token = GetDefault();

		GetDefault().Should().NotBe(token);
		var configuration = Configuration();
		configuration["access_token"] = token;
		(await _plugin.ValidateConfigurationAsync(configuration, TestToken)).IsValid.Should().BeTrue();
	}

	[Fact]
	public void ConnectionDetails_ContainUrlAndHeader()
	{
		_plugin.GetConnectionDetails(Configuration()).Should().BeEquivalentTo(
		[
			new PluginConnectionDetail("URL (Streamable HTTP)", Endpoint.ToString()),
			new PluginConnectionDetail("HTTP header (Authorization)", $"Authorization: Bearer {Token}", IsSecret: true)
		]);
	}

	[Fact]
	public void ConnectionDetails_WithoutToken_ContainOnlyUrl()
	{
		_plugin.GetConnectionDetails(new Dictionary<string, string>()).Should().ContainSingle()
			.Which.Value.Should().Be("http://localhost:47821/mcp");
	}

	private async Task AttachAsync(bool allowSubmission = false)
	{
		(await _plugin.InitializeAsync(Configuration(allowSubmission), TestToken)).Should().BeTrue();
		await _plugin.AttachAsync(_host.Object, TestToken);
	}

	private Task<McpClient> ConnectAsync()
	{
		var transport = new HttpClientTransport(new HttpClientTransportOptions
		{
			Endpoint = Endpoint,
			TransportMode = HttpTransportMode.StreamableHttp,
			AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = $"Bearer {Token}" }
		});
		return McpClient.CreateAsync(transport, cancellationToken: TestToken);
	}

	private async Task<HttpStatusCode> SendAsync(Action<HttpRequestMessage>? change = null, string body = Initialize)
	{
		using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
		request.Content = new StringContent(body, Encoding.UTF8, "application/json");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
		request.Headers.Accept.ParseAdd("application/json, text/event-stream");
		change?.Invoke(request);
		using var response = await _http.SendAsync(request, TestToken);
		return response.StatusCode;
	}

	private static string TextOf(CallToolResult result) => result.Content.OfType<TextContentBlock>().Single().Text;

	private async Task<CallToolResult> CallAsync(string tool, Dictionary<string, object?> arguments)
	{
		await using var client = await ConnectAsync();
		return await client.CallToolAsync(tool, arguments, cancellationToken: TestToken);
	}

	[Fact]
	public void PublicConstructor_IsTheOnlyOneThePluginLoaderCanUse()
	{
		typeof(McpPlugin).GetConstructors().Should().ContainSingle()
			.Which.GetParameters().Should().ContainSingle();
	}

	[Fact]
	public async Task Initialize_WithoutToken_Fails()
	{
		(await _plugin.InitializeAsync(new Dictionary<string, string>(), TestToken)).Should().BeFalse();
	}

	[Fact]
	public async Task Initialize_DoesNotListen()
	{
		// The CLI initializes plugins too, and must not start listening
		await _plugin.InitializeAsync(Configuration(), TestToken);

		await this.Invoking(t => t.SendAsync()).Should().ThrowAsync<HttpRequestException>();
	}

	[Fact]
	public async Task Attach_Twice_KeepsServing()
	{
		await AttachAsync();
		await _plugin.AttachAsync(_host.Object, TestToken);

		(await SendAsync()).Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Shutdown_StopsListening()
	{
		await AttachAsync();

		await _plugin.ShutdownAsync();

		await this.Invoking(t => t.SendAsync()).Should().ThrowAsync<HttpRequestException>();
	}

	[Fact]
	public async Task Attach_PortTaken_SaysWhichOne()
	{
		using var other = new HttpListener();
		other.Prefixes.Add($"http://localhost:{_port}/");
		other.Start();

		await this.Invoking(t => t.AttachAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{_port}*");
	}

	[Fact]
	public async Task Request_IsAnswered()
	{
		await AttachAsync();

		(await SendAsync()).Should().Be(HttpStatusCode.OK);
		(await SendAsync(body: Initialized)).Should().Be(HttpStatusCode.Accepted);
		(await SendAsync(r => r.Headers.Add("Origin", "http://localhost:3000"))).Should().Be(HttpStatusCode.OK);
	}

	[Fact]
	public async Task Request_ThatIsWrong_IsRejected()
	{
		await AttachAsync();

		(await SendAsync(r => r.Headers.Authorization = null)).Should().Be(HttpStatusCode.Unauthorized);
		(await SendAsync(r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong"))).Should().Be(HttpStatusCode.Unauthorized);
		(await SendAsync(r => r.Headers.Add("Origin", "https://example.com"))).Should().Be(HttpStatusCode.Forbidden);
		(await SendAsync(r => r.RequestUri = new Uri(Endpoint, "/other"))).Should().Be(HttpStatusCode.NotFound);
		(await SendAsync(r => r.Headers.Accept.Clear())).Should().Be(HttpStatusCode.NotAcceptable);
		(await SendAsync(body: "{")).Should().Be(HttpStatusCode.BadRequest);
		(await SendAsync(r =>
		{
			r.Method = HttpMethod.Get;
			r.Content = null;
		})).Should().Be(HttpStatusCode.MethodNotAllowed);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Tools_IncludeSubmissionOnlyWhenAllowed(bool allowed)
	{
		await AttachAsync(allowed);
		await using var client = await ConnectAsync();

		var tools = await client.ListToolsAsync(cancellationToken: TestToken);

		tools.Should().HaveCount(allowed ? 13 : 12);
		tools.Any(t => t.Name == "submit_worklogs").Should().Be(allowed);
		client.ServerInstructions.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public async Task Tools_RequireOnlyWhatHasNoDefault()
	{
		await AttachAsync();
		await using var client = await ConnectAsync();

		var tools = await client.ListToolsAsync(cancellationToken: TestToken);

		Required("update_work_entry").Should().Equal("id");
		Required("add_work_entry").Should().Equal("start");
		Required("list_work_entries").Should().Equal("from", "to");
		Required("start_work").Should().BeEmpty();

		IEnumerable<string?> Required(string name)
		{
			var schema = tools.Single(t => t.Name == name).JsonSchema;
			return schema.TryGetProperty("required", out var required) ? required.EnumerateArray().Select(r => r.GetString()).ToList() : [];
		}
	}

	[Fact]
	public async Task ListWorkEntries_PassesTheDaysAndReturnsTheEntries()
	{
		var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(1));
		_host
			.Setup(h => h.GetWorkEntriesAsync(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 4), It.IsAny<CancellationToken>()))
			.ReturnsAsync([new RemoteWorkEntry(7, "PROJ-1", null, start, null)]);
		await AttachAsync();

		var result = await CallAsync("list_work_entries", new() { ["from"] = "2026-11-02", ["to"] = "2026-11-04" });

		result.IsError.Should().NotBe(true);
		TextOf(result).Should().Contain("PROJ-1").And.Contain("\"id\":7");
	}

	[Fact]
	public async Task AddWorkEntry_PassesTheTimes()
	{
		var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.FromHours(1));
		_host
			.Setup(h => h.AddWorkEntryAsync("PROJ-1", null, start, start.AddHours(1), It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult.Success());
		await AttachAsync();

		var result = await CallAsync("add_work_entry", new() { ["ticketId"] = "PROJ-1", ["start"] = "2026-11-04T09:00:00+01:00", ["end"] = "2026-11-04T10:00:00+01:00" });

		result.IsError.Should().NotBe(true);
	}

	[Fact]
	public async Task UpdateWorkEntry_ClearsWhatIsLeftOut()
	{
		_host
			.Setup(h => h.UpdateWorkEntryTextAsync(7, null, "Review", It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<RemoteWorkEntry>.Success(new RemoteWorkEntry(7, null, "Review", DateTimeOffset.UnixEpoch, null)));
		await AttachAsync();

		var result = await CallAsync("update_work_entry", new() { ["id"] = 7, ["description"] = "Review" });

		result.IsError.Should().NotBe(true);
		TextOf(result).Should().Contain("Review");
	}

	[Fact]
	public async Task Failure_ComesBackAsAnErrorWithTheReason()
	{
		_host
			.Setup(h => h.StartWorkAsync(null, null, It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult.Failure("Needs a ticket", PluginErrorCategory.Validation));
		await AttachAsync();

		var result = await CallAsync("start_work", []);

		result.IsError.Should().BeTrue();
		TextOf(result).Should().Be("Needs a ticket");
	}

	[Fact]
	public async Task ListWorklogProviders_NamesTheModes()
	{
		_host.Setup(h => h.GetWorklogProviders()).Returns([new RemoteWorklogProvider("goran", "Goran", WorklogSubmissionMode.Timed | WorklogSubmissionMode.Aggregated)]);
		await AttachAsync();

		TextOf(await CallAsync("list_worklog_providers", [])).Should().Contain("Timed, Aggregated");
	}

	[Fact]
	public async Task SubmitWorklogs_PassesTheModeByNameAndMarksRefusedWorklogs()
	{
		_host
			.Setup(h => h.SubmitWorklogsAsync(new DateOnly(2026, 11, 4), RemoteWorkedTimePeriod.Week, WorklogSubmissionMode.Aggregated, "goran", It.IsAny<CancellationToken>()))
			.ReturnsAsync(PluginResult<RemoteSubmissionResult>.Success(new RemoteSubmissionResult(2, 1, 1, [new RemoteSubmissionError(1, "No ticket")])));
		await AttachAsync(allowSubmission: true);

		var result = await CallAsync("submit_worklogs", new() { ["date"] = "2026-11-04", ["period"] = "Week", ["mode"] = "Aggregated", ["providerId"] = "goran" });

		result.IsError.Should().BeTrue();
		TextOf(result).Should().Contain("No ticket");
	}
}
