using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Infrastructure.Auth;

namespace WorkTracker.Infrastructure.Tests.Auth;

public class MsalTokenProviderFactoryTests
{
	private readonly MsalTokenProviderFactory _factory = new(NullLoggerFactory.Instance);
	private readonly string[] _scopes = ["https://graph.microsoft.com/.default"];

	[Fact]
	public void Create_WithValidParams_ReturnsNonNullProvider()
	{
		// Act
		var provider = _factory.Create("tenant-id", "client-id", _scopes);

		// Assert
		provider.Should().NotBeNull();
		provider.Should().BeOfType<MsalTokenProvider>();
	}

	[Fact]
	public void Create_DifferentCredentials_ReturnsDifferentInstances()
	{
		// Act
		var provider1 = _factory.Create("tenant-a", "client-a", _scopes);
		var provider2 = _factory.Create("tenant-b", "client-b", _scopes);

		// Assert
		provider1.Should().NotBeSameAs(provider2);
	}

	[Fact]
	public void Create_SameCredentialsTwice_ReturnsSeparateInstances()
	{
		// Act
		var provider1 = _factory.Create("tenant-id", "client-id", _scopes);
		var provider2 = _factory.Create("tenant-id", "client-id", _scopes);

		// Assert — factory creates new instances each time (no caching)
		provider1.Should().NotBeSameAs(provider2);
	}
}
