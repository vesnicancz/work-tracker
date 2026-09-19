using GitCredentialManager;
using Microsoft.Extensions.Logging;
using WorkTracker.Application.Services;

namespace WorkTracker.Infrastructure.Security;

/// <summary>
/// Stores secrets in the native OS credential store (Windows Credential Manager / macOS Keychain / Linux libsecret).
/// Settings.json contains only placeholders in "CS:{pluginId}:{fieldKey}" format.
/// </summary>
public sealed class CredentialStoreSecureStorage : ISecureStorage
{
	private const string Prefix = "CS:";
	private const string AccountName = "WorkTracker";
	private const string BackingStoreVariable = "GCM_CREDENTIAL_STORE";
	private const string SecretServiceStore = "secretservice";
	private readonly ICredentialStore _store;
	private readonly ILogger<CredentialStoreSecureStorage> _logger;

	public CredentialStoreSecureStorage(ILogger<CredentialStoreSecureStorage> logger)
	{
		_logger = logger;
		SelectLinuxBackingStore(logger);
		_store = CredentialManager.Create(AccountName);
	}

	/// <summary>
	/// Names a backing store on Linux, where the credential manager has no default and refuses to
	/// store anything until one is chosen - saving a plugin's token failed with "No credential
	/// store has been selected". Windows and macOS resolve to the Credential Manager and the
	/// Keychain by themselves and are left untouched.
	/// <para>
	/// A store the user configured - through <c>GCM_CREDENTIAL_STORE</c> or git's
	/// <c>credential.credentialStore</c> - always wins. The variable is set on this process only,
	/// and only before the store is created, because that is when the choice is read.
	/// </para>
	/// </summary>
	private static void SelectLinuxBackingStore(ILogger logger)
	{
		if (!OperatingSystem.IsLinux())
		{
			return;
		}

		try
		{
			using var context = CredentialManager.CreateContext(AccountName);

			if (!string.IsNullOrWhiteSpace(context.Settings.CredentialBackingStore))
			{
				return;
			}

			// The freedesktop Secret Service is what a desktop session provides - GNOME Keyring,
			// KWallet. Outside one (the CLI over SSH) there is nothing sensible to guess, so the
			// setting stays empty and the credential manager's own error lists every store it
			// accepts instead of us picking a worse one.
			if (!context.SessionManager.IsDesktopSession)
			{
				logger.LogWarning(
					"No credential store is configured and this is not a desktop session; " +
					"set {Variable} to store plugin secrets", BackingStoreVariable);
				return;
			}

			Environment.SetEnvironmentVariable(BackingStoreVariable, SecretServiceStore);
			logger.LogInformation("Storing plugin secrets in the {Store} credential store", SecretServiceStore);
		}
		catch (Exception ex)
		{
			// Only the choice failed; the store below still works if the platform has a default.
			logger.LogWarning(ex, "Could not determine which credential store to use");
		}
	}

	public string Protect(string plainText, string pluginId, string fieldKey)
	{
		if (string.IsNullOrEmpty(plainText))
		{
			return plainText;
		}

		if (plainText.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return plainText;
		}

		var target = BuildTarget(pluginId, fieldKey);

		try
		{
			_store.AddOrUpdate(target, AccountName, plainText);
			return $"{Prefix}{pluginId}:{fieldKey}";
		}
		catch (Exception ex)
		{
			_logger.LogError(ex, "Failed to store credential for {PluginId}:{FieldKey}", pluginId, fieldKey);
			throw new InvalidOperationException(
				$"Failed to store credential for plugin '{pluginId}' in the OS credential store. " +
				"Ensure your system's credential manager is available and working.", ex);
		}
	}

	public string Unprotect(string protectedText)
	{
		if (string.IsNullOrEmpty(protectedText))
		{
			return protectedText;
		}

		if (!protectedText.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return protectedText;
		}

		var keyPart = protectedText[Prefix.Length..];
		var separatorIndex = keyPart.IndexOf(':');
		if (separatorIndex < 0)
		{
			_logger.LogWarning("Invalid credential placeholder format (length={Length})", protectedText.Length);
			return protectedText;
		}

		var pluginId = keyPart[..separatorIndex];
		var fieldKey = keyPart[(separatorIndex + 1)..];
		var target = BuildTarget(pluginId, fieldKey);

		try
		{
			var credential = _store.Get(target, AccountName);
			if (credential != null)
			{
				return credential.Password;
			}

			_logger.LogWarning("Credential not found for {PluginId}:{FieldKey}", pluginId, fieldKey);
			return protectedText;
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to retrieve credential for {PluginId}:{FieldKey}", pluginId, fieldKey);
			return protectedText;
		}
	}

	public void Remove(string pluginId, string fieldKey)
	{
		var target = BuildTarget(pluginId, fieldKey);

		try
		{
			_store.Remove(target, AccountName);
		}
		catch (Exception ex)
		{
			_logger.LogWarning(ex, "Failed to remove credential for {PluginId}:{FieldKey}", pluginId, fieldKey);
		}
	}

	private static string BuildTarget(string pluginId, string fieldKey) =>
		$"worktracker://{pluginId}/{fieldKey}";
}