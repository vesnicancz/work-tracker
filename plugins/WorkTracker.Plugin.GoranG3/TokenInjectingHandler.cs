using System.Net.Http.Headers;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Plugin.GoranG3;

/// <summary>
/// DelegatingHandler that injects a fresh Bearer token from <see cref="ITokenProvider"/> into each HTTP request.
/// </summary>
internal sealed class TokenInjectingHandler(ITokenProvider tokenProvider) : DelegatingHandler(new HttpClientHandler())
{
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
	{
		var token = await tokenProvider.AcquireTokenSilentAsync(cancellationToken);
		if (token == null)
		{
			throw new InvalidOperationException("Authentication token expired — please use Test Connection in Settings to re-authenticate.");
		}

		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		return await base.SendAsync(request, cancellationToken);
	}
}
