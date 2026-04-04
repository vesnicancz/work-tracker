using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// DelegatingHandler that injects a fresh Bearer token from MSAL into each HTTP request.
/// </summary>
internal sealed class TokenInjectingHandler : DelegatingHandler
{
	private readonly IPublicClientApplication _msalApp;
	private readonly string[] _scopes;
	private readonly ILogger? _logger;

	public TokenInjectingHandler(IPublicClientApplication msalApp, string[] scopes, ILogger? logger)
		: base(new HttpClientHandler())
	{
		_msalApp = msalApp;
		_scopes = scopes;
		_logger = logger;
	}

	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var token = await AcquireTokenAsync(cancellationToken);
		request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
		return await base.SendAsync(request, cancellationToken);
	}

	private async Task<string> AcquireTokenAsync(CancellationToken cancellationToken)
	{
		var accounts = await _msalApp.GetAccountsAsync().ConfigureAwait(false);
		var firstAccount = accounts.FirstOrDefault();

		if (firstAccount == null)
		{
			throw new InvalidOperationException("Not authenticated. Please use Test Connection in Settings to sign in first.");
		}

		try
		{
			var silentResult = await _msalApp
				.AcquireTokenSilent(_scopes, firstAccount)
				.ExecuteAsync(cancellationToken)
				.ConfigureAwait(false);
			return silentResult.AccessToken;
		}
		catch (MsalUiRequiredException)
		{
			throw new InvalidOperationException("Authentication expired. Please use Test Connection in Settings to sign in again.");
		}
	}
}
