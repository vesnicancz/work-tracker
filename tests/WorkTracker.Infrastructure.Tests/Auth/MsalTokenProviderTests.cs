using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Moq;
using WorkTracker.Infrastructure.Auth;

namespace WorkTracker.Infrastructure.Tests.Auth;

public class MsalTokenProviderTests
{
	private readonly ILogger _logger = NullLogger.Instance;
	private readonly string[] _scopes = ["https://graph.microsoft.com/.default"];

	[Fact]
	public async Task AcquireTokenSilentAsync_NoAccounts_ReturnsNull()
	{
		// Arrange
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync(It.IsAny<string>()))
			.ReturnsAsync(Array.Empty<IAccount>());
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync(Array.Empty<IAccount>());

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenSilentAsync(TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
	}

	[Fact]
	public async Task AcquireTokenSilentAsync_GetAccountsThrows_ReturnsNull()
	{
		// Arrange
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ThrowsAsync(new InvalidOperationException("MSAL error"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenSilentAsync(TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
	}

	[Fact]
	public async Task AcquireTokenInteractiveAsync_SilentReturnsNull_AttemptsDeviceCode()
	{
		// Arrange
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync(Array.Empty<IAccount>());

		// AcquireTokenWithDeviceCode is called on the real interface — it returns a sealed builder.
		// We verify the method is at least invoked by letting it throw,
		// which exercises the catch path and returns null.
		mockApp.Setup(a => a.AcquireTokenWithDeviceCode(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<Func<DeviceCodeResult, Task>>()))
			.Throws(new MsalServiceException("error", "Device code failed"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenInteractiveAsync(null, TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
		mockApp.Verify(a => a.AcquireTokenWithDeviceCode(
			It.IsAny<IEnumerable<string>>(),
			It.IsAny<Func<DeviceCodeResult, Task>>()), Times.Once);
	}

	[Fact]
	public async Task AcquireTokenInteractiveAsync_DeviceCodeThrows_ReturnsNull()
	{
		// Arrange
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync(Array.Empty<IAccount>());
		mockApp.Setup(a => a.AcquireTokenWithDeviceCode(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<Func<DeviceCodeResult, Task>>()))
			.Throws(new Exception("Network error"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenInteractiveAsync(
			new Progress<string>(), TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
	}

	[Fact]
	public async Task AcquireTokenSilentAsync_WithAccount_CallsAcquireTokenSilent()
	{
		// Arrange
		var mockAccount = new Mock<IAccount>();
		mockAccount.Setup(a => a.Username).Returns("user@test.com");

		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync([mockAccount.Object]);

		// AcquireTokenSilent returns a sealed builder — we can't mock ExecuteAsync on it.
		// We verify the method is invoked with the right account by letting it throw
		// MsalUiRequiredException, which the provider catches and returns null.
		mockApp.Setup(a => a.AcquireTokenSilent(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<IAccount>()))
			.Throws(new MsalUiRequiredException("error", "UI required"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenSilentAsync(TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
		mockApp.Verify(a => a.AcquireTokenSilent(
			It.IsAny<IEnumerable<string>>(),
			mockAccount.Object), Times.Once);
	}

	[Fact]
	public async Task AcquireTokenSilentAsync_MsalUiRequiredException_ReturnsNull()
	{
		// Arrange
		var mockAccount = new Mock<IAccount>();
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync([mockAccount.Object]);
		mockApp.Setup(a => a.AcquireTokenSilent(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<IAccount>()))
			.Throws(new MsalUiRequiredException("error", "Need interactive login"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenSilentAsync(TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
	}

	[Fact]
	public async Task AcquireTokenInteractiveAsync_SilentHasAccount_ButSilentThrows_FallsBackToDeviceCode()
	{
		// Arrange
		var mockAccount = new Mock<IAccount>();
		var mockApp = new Mock<IPublicClientApplication>();
		mockApp.Setup(a => a.GetAccountsAsync())
			.ReturnsAsync([mockAccount.Object]);

		// Silent throws MsalUiRequiredException -> returns null from silent
		mockApp.Setup(a => a.AcquireTokenSilent(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<IAccount>()))
			.Throws(new MsalUiRequiredException("error", "UI required"));

		// Device code is then attempted
		mockApp.Setup(a => a.AcquireTokenWithDeviceCode(
				It.IsAny<IEnumerable<string>>(),
				It.IsAny<Func<DeviceCodeResult, Task>>()))
			.Throws(new MsalServiceException("error", "Device code also failed"));

		var provider = new MsalTokenProvider(mockApp.Object, _scopes, _logger);

		// Act
		var token = await provider.AcquireTokenInteractiveAsync(null, TestContext.Current.CancellationToken);

		// Assert
		token.Should().BeNull();
		mockApp.Verify(a => a.AcquireTokenWithDeviceCode(
			It.IsAny<IEnumerable<string>>(),
			It.IsAny<Func<DeviceCodeResult, Task>>()), Times.Once);
	}
}
