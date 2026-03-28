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
	private readonly ICredentialStore _store;
	private readonly ILogger<CredentialStoreSecureStorage> _logger;

	public CredentialStoreSecureStorage(ILogger<CredentialStoreSecureStorage> logger)
	{
		_store = CredentialManager.Create(AccountName);
		_logger = logger;
	}

	public string Protect(string plainText, string pluginId, string fieldKey)
	{
		if (string.IsNullOrEmpty(plainText))
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
			_logger.LogWarning(ex, "Failed to store credential for {PluginId}:{FieldKey}, storing as plaintext", pluginId, fieldKey);
			return plainText;
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
			_logger.LogWarning("Invalid credential placeholder format: {Value}", protectedText);
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