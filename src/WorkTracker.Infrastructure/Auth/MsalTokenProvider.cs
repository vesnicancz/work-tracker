using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensibility;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Auth;

public sealed class MsalTokenProvider : ITokenProvider
{
	private static readonly TimeSpan BrowserSignInTimeout = TimeSpan.FromMinutes(5);

	private readonly IPublicClientApplication _msalApp;
	private readonly Func<string, IPublicClientApplication> _createLoopbackApp;
	private readonly string[] _scopes;
	private readonly InteractiveAuthFlow _interactiveFlow;
	private readonly ILogger _logger;

	/// <param name="createLoopbackApp">
	/// Builds an app bound to the given loopback redirect URI and sharing the token cache with <paramref name="msalApp"/>.
	/// The redirect URI carries a port picked per sign-in, so the system browser flow needs a fresh app each time.
	/// </param>
	internal MsalTokenProvider(
		IPublicClientApplication msalApp,
		Func<string, IPublicClientApplication> createLoopbackApp,
		string[] scopes,
		InteractiveAuthFlow interactiveFlow,
		ILogger logger)
	{
		_msalApp = msalApp;
		_createLoopbackApp = createLoopbackApp;
		_scopes = scopes;
		_interactiveFlow = interactiveFlow;
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
			var result = _interactiveFlow switch
			{
				InteractiveAuthFlow.DeviceCode => await AcquireWithDeviceCodeAsync(progress, cancellationToken),
				InteractiveAuthFlow.SystemBrowser => await AcquireWithSystemBrowserAsync(progress, cancellationToken),
				_ => throw new InvalidOperationException($"Unsupported interactive auth flow: {_interactiveFlow}")
			};

			return result.AccessToken;
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Interactive token acquisition failed");
			return null;
		}
	}

	private Task<AuthenticationResult> AcquireWithDeviceCodeAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		return _msalApp.AcquireTokenWithDeviceCode(_scopes, callback =>
		{
			progress?.Report($"⏳ Open browser and enter code: {callback.UserCode}\n{callback.VerificationUrl}");
			OpenBrowser(new Uri(callback.VerificationUrl));
			return Task.CompletedTask;
		}).ExecuteAsync(cancellationToken);
	}

	private async Task<AuthenticationResult> AcquireWithSystemBrowserAsync(IProgress<string>? progress, CancellationToken cancellationToken)
	{
		var redirectUri = $"http://localhost:{SystemBrowserWebUi.GetFreeLoopbackPort()}";
		var app = _createLoopbackApp(redirectUri);

		progress?.Report("⏳ Complete the sign-in in your browser…");

		var webUi = new SystemBrowserWebUi(uri =>
		{
			if (!OpenBrowser(uri))
			{
				progress?.Report($"⏳ Open this address in your browser to sign in:\n{uri}");
			}
		}, BrowserSignInTimeout);

		return await app.AcquireTokenInteractive(_scopes)
			.WithCustomWebUi(webUi)
			.ExecuteAsync(cancellationToken);
	}

	private bool OpenBrowser(Uri uri)
	{
		try
		{
			System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
			{
				FileName = uri.ToString(),
				UseShellExecute = true
			});
			return true;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Could not open browser automatically");
			return false;
		}
	}
}
