namespace WorkTracker.Plugin.Abstractions;

/// <summary>
/// How <see cref="ITokenProvider.AcquireTokenInteractiveAsync"/> signs the user in when no cached token is available.
/// </summary>
public enum InteractiveAuthFlow
{
	/// <summary>
	/// Device code flow — the user enters a code at the verification URL. Requires "Allow public client flows" in the Entra app registration.
	/// </summary>
	DeviceCode,

	/// <summary>
	/// Authorization code flow with PKCE in the system browser, redirecting back to a loopback listener.
	/// Requires the "Mobile and desktop applications" platform with redirect URI <c>http://localhost</c> in the Entra app registration.
	/// </summary>
	SystemBrowser
}
