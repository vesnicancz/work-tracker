using System.Net;
using FluentAssertions;
using WorkTracker.Infrastructure.Auth;

namespace WorkTracker.Infrastructure.Tests.Auth;

public class SystemBrowserWebUiTests
{
	private static readonly Uri AuthorizationUri = new("https://login.microsoftonline.com/tenant/oauth2/v2.0/authorize?client_id=x");

	private readonly Uri _redirectUri = new($"http://localhost:{SystemBrowserWebUi.GetFreeLoopbackPort()}");

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_RedirectWithCode_ReturnsRedirectUrl()
	{
		// Arrange
		using var client = new HttpClient();
		var webUi = new SystemBrowserWebUi(_ => SimulateRedirect(client, "?code=abc&state=xyz"), TimeSpan.FromSeconds(30));

		// Act
		var result = await webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, TestContext.Current.CancellationToken);

		// Assert
		result.Query.Should().Be("?code=abc&state=xyz");
	}

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_OpensBrowserWithAuthorizationUri()
	{
		// Arrange
		using var client = new HttpClient();
		Uri? opened = null;
		var webUi = new SystemBrowserWebUi(uri =>
		{
			opened = uri;
			SimulateRedirect(client, "?code=abc");
		}, TimeSpan.FromSeconds(30));

		// Act
		await webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, TestContext.Current.CancellationToken);

		// Assert
		opened.Should().Be(AuthorizationUri);
	}

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_StrayRequestFirst_IgnoresItAndWaitsForCode()
	{
		// Arrange
		using var client = new HttpClient();
		HttpStatusCode? strayStatus = null;
		var webUi = new SystemBrowserWebUi(uri =>
		{
			_ = Task.Run(async () =>
			{
				using var stray = await client.GetAsync(new Uri(_redirectUri, "/favicon.ico"));
				strayStatus = stray.StatusCode;
				await client.GetAsync(new Uri(_redirectUri, "?code=abc"));
			});
		}, TimeSpan.FromSeconds(30));

		// Act
		var result = await webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, TestContext.Current.CancellationToken);

		// Assert
		result.Query.Should().Be("?code=abc");
		strayStatus.Should().Be(HttpStatusCode.NotFound);
	}

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_RedirectWithError_ReturnsRedirectUrlForMsalToParse()
	{
		// Arrange
		using var client = new HttpClient();
		var webUi = new SystemBrowserWebUi(_ => SimulateRedirect(client, "?error=access_denied"), TimeSpan.FromSeconds(30));

		// Act
		var result = await webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, TestContext.Current.CancellationToken);

		// Assert
		result.Query.Should().Be("?error=access_denied");
	}

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_NoRedirectWithinTimeout_ThrowsTimeoutException()
	{
		// Arrange
		var webUi = new SystemBrowserWebUi(_ => { }, TimeSpan.FromMilliseconds(100));

		// Act
		var act = () => webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, TestContext.Current.CancellationToken);

		// Assert
		await act.Should().ThrowAsync<TimeoutException>();
	}

	[Fact]
	public async Task AcquireAuthorizationCodeAsync_Cancelled_ThrowsOperationCanceledException()
	{
		// Arrange
		using var cts = new CancellationTokenSource();
		var webUi = new SystemBrowserWebUi(_ => cts.Cancel(), TimeSpan.FromSeconds(30));

		// Act
		var act = () => webUi.AcquireAuthorizationCodeAsync(AuthorizationUri, _redirectUri, cts.Token);

		// Assert
		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	private void SimulateRedirect(HttpClient client, string query)
	{
		_ = Task.Run(() => client.GetAsync(new Uri(_redirectUri, query)));
	}
}
