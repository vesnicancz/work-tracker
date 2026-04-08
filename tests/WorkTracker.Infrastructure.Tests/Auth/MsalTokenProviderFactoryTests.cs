using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Infrastructure.Auth;

namespace WorkTracker.Infrastructure.Tests.Auth;

public class MsalTokenProviderFactoryTests
{
	private readonly MsalTokenProviderFactory _factory = new(NullLoggerFactory.Instance);
	private readonly string[] _scopes = ["https://graph.microsoft.com/.default"];

	[Fact]
	public async Task CreateAsync_WithValidParams_ReturnsNonNullProvider()
	{
		var provider = await _factory.CreateAsync("tenant-id", "client-id", _scopes);

		provider.Should().NotBeNull();
		provider.Should().BeOfType<MsalTokenProvider>();
	}

	[Fact]
	public async Task CreateAsync_DifferentCredentials_ReturnsDifferentInstances()
	{
		var provider1 = await _factory.CreateAsync("tenant-a", "client-a", _scopes);
		var provider2 = await _factory.CreateAsync("tenant-b", "client-b", _scopes);

		provider1.Should().NotBeSameAs(provider2);
	}

	[Fact]
	public async Task CreateAsync_SameCredentialsTwice_ReturnsSeparateInstances()
	{
		var provider1 = await _factory.CreateAsync("tenant-id", "client-id", _scopes);
		var provider2 = await _factory.CreateAsync("tenant-id", "client-id", _scopes);

		provider1.Should().NotBeSameAs(provider2);
	}
}
