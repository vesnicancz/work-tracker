namespace WorkTracker.Application.Services;

/// <summary>
/// Stores and retrieves sensitive configuration values (API tokens, passwords)
/// using the OS credential store.
/// </summary>
public interface ISecureStorage
{
	/// <summary>
	/// Stores the secret in the credential store and returns a placeholder token for settings.json.
	/// </summary>
	string Protect(string plainText, string pluginId, string fieldKey);

	/// <summary>
	/// Retrieves the secret from secure storage if the value is a protected placeholder.
	/// Plaintext values pass through unchanged.
	/// </summary>
	string Unprotect(string protectedText);

	/// <summary>
	/// Removes a secret from the credential store.
	/// </summary>
	void Remove(string pluginId, string fieldKey);
}