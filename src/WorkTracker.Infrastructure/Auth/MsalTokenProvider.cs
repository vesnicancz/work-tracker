using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Auth;

public sealed class MsalTokenProvider : ITokenProvider
{
	private readonly IPublicClientApplication _msalApp;
	private readonly string[] _scopes;
	private readonly ILogger _logger;

	internal MsalTokenProvider(IPublicClientApplication msalApp, string[] scopes, ILogger logger)
	{
		_msalApp = msalApp;
		_scopes = scopes;
		_logger = logger;
	}

	public async Task<string?> AcquireTokenSilentAsync(CancellationToken cancellationToken)
	{
		try
		{
			var accounts = await _msalApp.GetAccountsAsync();
			var account = accounts.FirstOrDefault();

			if (account != null)
			{
				var result = await _msalApp.AcquireTokenSilent(_scopes, account)
					.ExecuteAsync(cancellationToken);
				return result.AccessToken;
			}
		}
		catch (MsalUiRequiredException)
		{
			// Silent acquisition failed — interactive sign-in needed
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Silent token acquisition failed");
		}

		return null;
	}

	public async Task<string?> AcquireTokenInteractiveAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		var token = await AcquireTokenSilentAsync(cancellationToken);
		if (token != null)
		{
			return token;
		}

		try
		{
			var result = await _msalApp.AcquireTokenWithDeviceCode(_scopes, callback =>
			{
				progress?.Report($"\u23f3 Open browser and enter code: {callback.UserCode}\n{callback.VerificationUrl}");

				try
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
					{
						FileName = callback.VerificationUrl.ToString(),
						UseShellExecute = true
					});
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Could not open browser automatically");
				}

				return Task.CompletedTask;
			}).ExecuteAsync(cancellationToken);

			return result.AccessToken;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Interactive token acquisition failed");
			return null;
		}
	}
}
