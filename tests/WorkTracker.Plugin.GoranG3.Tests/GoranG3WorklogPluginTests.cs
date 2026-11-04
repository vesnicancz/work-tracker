using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3.Tests;

public class GoranG3WorklogPluginTests : IAsyncDisposable
{
	private readonly GoranG3WorklogPlugin _plugin;

	private static readonly Dictionary<string, string> ValidConfig = new()
	{
		["GoranBaseUrl"] = "https://moonfish-g3.goran.cz",
		["ProjectCode"] = "000.GOR",
		["EntraClientId"] = "00000000-0000-0000-0000-000000000000",
		["EntraTenantId"] = "00000000-0000-0000-0000-000000000001",
		["EntraScopes"] = "api://test/Mcp.Access"
	};

	public GoranG3WorklogPluginTests()
	{
		_plugin = new GoranG3WorklogPlugin(
			NullLogger<GoranG3WorklogPlugin>.Instance,
			new MockTokenProviderFactory("fake-token"));
	}

	public async ValueTask DisposeAsync()
	{
		await _plugin.DisposeAsync();
	}

	#region Metadata

	[Fact]
	public void Metadata_HasCorrectId()
	{
		_plugin.Metadata.Id.Should().Be("gorang3.worklog");
	}

	[Fact]
	public void Metadata_HasCorrectNameAndVersion()
	{
		_plugin.Metadata.Name.Should().Be("Goran G3 Timesheets");
		_plugin.Metadata.Version.Should().Be(new Version(2, 0, 0));
	}

	#endregion

	#region Configuration Fields

	[Fact]
	public void GetConfigurationFields_ReturnsExpectedFields()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Should().HaveCount(7);
		fields.Select(f => f.Key).Should().BeEquivalentTo(
		[
			"GoranBaseUrl", "ProjectCode", "ProjectPhaseCode", "Tags",
			"EntraClientId", "EntraTenantId", "EntraScopes"
		]);
	}

	[Fact]
	public void GetConfigurationFields_RequiredFieldsFlaggedCorrectly()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Single(f => f.Key == "GoranBaseUrl").IsRequired.Should().BeTrue();
		fields.Single(f => f.Key == "ProjectCode").IsRequired.Should().BeTrue();
		fields.Single(f => f.Key == "EntraClientId").IsRequired.Should().BeTrue();
		fields.Single(f => f.Key == "EntraTenantId").IsRequired.Should().BeTrue();
		fields.Single(f => f.Key == "EntraScopes").IsRequired.Should().BeTrue();
	}

	[Fact]
	public void GetConfigurationFields_OptionalFieldsFlaggedCorrectly()
	{
		var fields = _plugin.GetConfigurationFields();

		fields.Single(f => f.Key == "ProjectPhaseCode").IsRequired.Should().BeFalse();
		fields.Single(f => f.Key == "Tags").IsRequired.Should().BeFalse();
	}

	#endregion

	#region Initialization

	[Fact]
	public async Task InitializeAsync_ValidConfig_ReturnsTrue()
	{
		var result = await _plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.Should().BeTrue();
	}

	[Fact]
	public async Task InitializeAsync_MissingRequiredConfig_ReturnsFalse()
	{
		var result = await _plugin.InitializeAsync(new Dictionary<string, string>(), TestContext.Current.CancellationToken);

		result.Should().BeFalse();
	}

	[Fact]
	public async Task InitializeAsync_MissingGoranBaseUrl_ReturnsFalse()
	{
		var config = new Dictionary<string, string>(ValidConfig);
		config.Remove("GoranBaseUrl");

		var result = await _plugin.InitializeAsync(config, TestContext.Current.CancellationToken);

		result.Should().BeFalse();
	}

	[Fact]
	public async Task InitializeAsync_SilentAuthFails_StillSucceeds()
	{
		var plugin = new GoranG3WorklogPlugin(
			NullLogger<GoranG3WorklogPlugin>.Instance,
			new MockTokenProviderFactory(null));

		var result = await plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		result.Should().BeTrue("silent auth failure should not prevent initialization");
		await plugin.DisposeAsync();
	}

	#endregion

	#region Connecting

	private static readonly Dictionary<string, string> UnreachableServerConfig = new(ValidConfig)
	{
		["GoranBaseUrl"] = "https://127.0.0.1:1"
	};

	private static PluginWorklogEntry Worklog() => new()
	{
		TicketId = "A-1",
		StartTime = new DateTime(2026, 11, 2, 9, 0, 0),
		EndTime = new DateTime(2026, 11, 2, 10, 0, 0),
		DurationMinutes = 60
	};

	[Fact]
	public async Task UploadWorklogAsync_NotSignedIn_ReportsItWithoutThrowing()
	{
		var tokenProvider = new SwitchableTokenProvider();
		await using var plugin = new GoranG3WorklogPlugin(NullLogger<GoranG3WorklogPlugin>.Instance, new SingleTokenProviderFactory(tokenProvider));
		await plugin.InitializeAsync(UnreachableServerConfig, TestContext.Current.CancellationToken);

		var result = await plugin.UploadWorklogAsync(Worklog(), TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
		result.ErrorCategory.Should().Be(PluginErrorCategory.Authentication);
	}

	[Fact]
	public async Task UploadWorklogAsync_SignedInSinceInitialization_TriesToConnect()
	{
		var tokenProvider = new SwitchableTokenProvider();
		await using var plugin = new GoranG3WorklogPlugin(NullLogger<GoranG3WorklogPlugin>.Instance, new SingleTokenProviderFactory(tokenProvider));
		await plugin.InitializeAsync(UnreachableServerConfig, TestContext.Current.CancellationToken);
		tokenProvider.ResetSilentCalls();

		tokenProvider.Token = "fake-token";
		var result = await plugin.UploadWorklogAsync(Worklog(), TestContext.Current.CancellationToken);

		tokenProvider.SilentCalls.Should().BeGreaterThan(0, "a missing connection is tried again on each operation");
		result.IsFailure.Should().BeTrue("the server is not reachable");
	}

	[Fact]
	public async Task UploadWorklogsAsync_ServerUnavailable_ReportsItWithoutThrowing()
	{
		var tokenProvider = new SwitchableTokenProvider { Token = "fake-token" };
		await using var plugin = new GoranG3WorklogPlugin(NullLogger<GoranG3WorklogPlugin>.Instance, new SingleTokenProviderFactory(tokenProvider));
		await plugin.InitializeAsync(UnreachableServerConfig, TestContext.Current.CancellationToken);

		var result = await plugin.UploadWorklogsAsync([Worklog()], WorklogSubmissionMode.Timed, TestContext.Current.CancellationToken);

		result.IsFailure.Should().BeTrue();
	}

	#endregion

	#region Lifecycle

	[Fact]
	public async Task DisposeAsync_CompletesWithoutError()
	{
		await _plugin.InitializeAsync(ValidConfig, TestContext.Current.CancellationToken);

		var act = async () => await _plugin.DisposeAsync();

		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task DisposeAsync_CanBeCalledMultipleTimes()
	{
		await _plugin.DisposeAsync();
		await _plugin.DisposeAsync();
	}

	[Fact]
	public async Task DisposeAsync_WithoutInitialization_CompletesWithoutError()
	{
		var plugin = new GoranG3WorklogPlugin(
			NullLogger<GoranG3WorklogPlugin>.Instance,
			new MockTokenProviderFactory("fake-token"));

		var act = async () => await plugin.DisposeAsync();

		await act.Should().NotThrowAsync();
	}

	#endregion
}

internal sealed class SwitchableTokenProvider : ITokenProvider
{
	public string? Token { get; set; }

	public int SilentCalls { get; private set; }

	public void ResetSilentCalls() => SilentCalls = 0;

	public Task<string?> AcquireTokenSilentAsync(CancellationToken cancellationToken)
	{
		SilentCalls++;
		return Task.FromResult(Token);
	}

	public Task<string?> AcquireTokenInteractiveAsync(IProgress<string>? progress, CancellationToken cancellationToken)
		=> Task.FromResult(Token);
}

internal sealed class SingleTokenProviderFactory(ITokenProvider tokenProvider) : ITokenProviderFactory
{
	public Task<ITokenProvider> CreateAsync(string tenantId, string clientId, string[] scopes, InteractiveAuthFlow interactiveFlow)
		=> Task.FromResult(tokenProvider);
}

internal sealed class MockTokenProvider(string? token) : ITokenProvider
{
	public Task<string?> AcquireTokenSilentAsync(CancellationToken cancellationToken)
		=> Task.FromResult(token);

	public Task<string?> AcquireTokenInteractiveAsync(IProgress<string>? progress, CancellationToken cancellationToken)
		=> Task.FromResult(token);
}

internal sealed class MockTokenProviderFactory(string? token = "fake-token") : ITokenProviderFactory
{
	public Task<ITokenProvider> CreateAsync(string tenantId, string clientId, string[] scopes, InteractiveAuthFlow interactiveFlow)
		=> Task.FromResult<ITokenProvider>(new MockTokenProvider(token));
}
