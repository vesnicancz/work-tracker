using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Infrastructure.Security;

namespace WorkTracker.Infrastructure.Tests.Security;

/// <summary>
/// Integration tests for CredentialStoreSecureStorage.
/// These tests interact with the native OS credential store.
/// </summary>
[Trait("Category", "Integration")]
public class CredentialStoreSecureStorageTests : IDisposable
{
	private const string ProtectedPrefix = "CS:";

	private readonly CredentialStoreSecureStorage _sut;
	private readonly string _testPluginId = $"test.plugin.{Guid.NewGuid():N}";
	private readonly List<(string pluginId, string fieldKey)> _createdCredentials = [];

	public CredentialStoreSecureStorageTests()
	{
		_sut = new CredentialStoreSecureStorage(new Mock<ILogger<CredentialStoreSecureStorage>>().Object);
	}

	public void Dispose()
	{
		foreach (var (pluginId, fieldKey) in _createdCredentials)
		{
			try { _sut.Remove(pluginId, fieldKey); }
			catch { /* best effort cleanup */ }
		}
	}

	private string ProtectAndTrack(string plainText, string fieldKey)
	{
		_createdCredentials.Add((_testPluginId, fieldKey));
		return _sut.Protect(plainText, _testPluginId, fieldKey);
	}

	#region Protect

	[Fact]
	public void Protect_EmptyString_ReturnsEmpty()
	{
		var result = _sut.Protect(string.Empty, _testPluginId, "key");
		result.Should().BeEmpty();
	}

	[Fact]
	public void Protect_NullString_ReturnsNull()
	{
		var result = _sut.Protect(null!, _testPluginId, "key");
		result.Should().BeNull();
	}

	[Fact]
	public void Protect_AlreadyProtected_ReturnsUnchanged()
	{
		var alreadyProtected = $"{ProtectedPrefix}some.plugin:someKey";
		var result = _sut.Protect(alreadyProtected, _testPluginId, "key");
		result.Should().Be(alreadyProtected);
	}

	[Fact]
	public void Protect_ValidPlaintext_ReturnsPlaceholder()
	{
		var result = ProtectAndTrack("my-secret-token", "apiKey");

		result.Should().Be($"{ProtectedPrefix}{_testPluginId}:apiKey");
	}

	#endregion

	#region Unprotect

	[Fact]
	public void Unprotect_EmptyString_ReturnsEmpty()
	{
		var result = _sut.Unprotect(string.Empty);
		result.Should().BeEmpty();
	}

	[Fact]
	public void Unprotect_PlaintextWithoutPrefix_ReturnsUnchanged()
	{
		var plaintext = "just-a-regular-value";
		var result = _sut.Unprotect(plaintext);
		result.Should().Be(plaintext);
	}

	[Fact]
	public void Unprotect_InvalidFormat_ReturnsUnchanged()
	{
		var invalid = $"{ProtectedPrefix}nocoloninsecondpart";
		var result = _sut.Unprotect(invalid);
		result.Should().Be(invalid);
	}

	[Fact]
	public void Unprotect_NonExistentCredential_ReturnsPlaceholder()
	{
		var placeholder = $"{ProtectedPrefix}nonexistent.plugin:nonexistentKey";
		var result = _sut.Unprotect(placeholder);
		result.Should().Be(placeholder);
	}

	#endregion

	#region Round-trip

	[Fact]
	public void ProtectThenUnprotect_ReturnsOriginalValue()
	{
		var secret = "super-secret-api-token-12345";
		var placeholder = ProtectAndTrack(secret, "token");

		var result = _sut.Unprotect(placeholder);

		result.Should().Be(secret);
	}

	[Fact]
	public void ProtectThenUnprotect_MultipleFields_EachReturnsCorrectValue()
	{
		var secret1 = "token-1";
		var secret2 = "token-2";

		var placeholder1 = ProtectAndTrack(secret1, "field1");
		var placeholder2 = ProtectAndTrack(secret2, "field2");

		_sut.Unprotect(placeholder1).Should().Be(secret1);
		_sut.Unprotect(placeholder2).Should().Be(secret2);
	}

	#endregion

	#region Remove

	[Fact]
	public void Remove_ExistingCredential_DoesNotThrow()
	{
		ProtectAndTrack("secret", "toRemove");

		var act = () => _sut.Remove(_testPluginId, "toRemove");
		act.Should().NotThrow();
	}

	[Fact]
	public void Remove_NonExistentCredential_DoesNotThrow()
	{
		var act = () => _sut.Remove(_testPluginId, "nonexistent");
		act.Should().NotThrow();
	}

	[Fact]
	public void Remove_ThenUnprotect_ReturnsPlaceholder()
	{
		var placeholder = ProtectAndTrack("secret", "removable");
		_sut.Remove(_testPluginId, "removable");

		var result = _sut.Unprotect(placeholder);

		// After removal, credential is gone — returns placeholder as-is
		result.Should().Be(placeholder);
	}

	#endregion
}
