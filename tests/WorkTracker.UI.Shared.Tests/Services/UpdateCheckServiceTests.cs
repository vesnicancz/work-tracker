using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class UpdateCheckServiceTests
{
	private readonly Mock<ISettingsService> _settingsService = new();
	private readonly Mock<ISystemNotificationService> _systemNotification = new();
	private readonly Mock<ILocalizationService> _localization = new();

	public UpdateCheckServiceTests()
	{
		var settings = new ApplicationSettings { CheckForUpdates = true };
		_settingsService.Setup(s => s.Settings).Returns(settings);
		_localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		_localization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
			.Returns((string key, object[] args) => string.Format(key, args));
	}

	private UpdateCheckService CreateService(string currentVersion, HttpMessageHandler handler)
	{
		var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
		return new UpdateCheckService(
			currentVersion,
			httpClient,
			_settingsService.Object,
			_systemNotification.Object,
			_localization.Object,
			NullLogger<UpdateCheckService>.Instance);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenDisabled_DoesNotMakeHttpCall()
	{
		_settingsService.Setup(s => s.Settings).Returns(new ApplicationSettings { CheckForUpdates = false });
		var handler = new FakeHandler(_ => throw new InvalidOperationException("Should not be called"));
		var sut = CreateService("1.0.0", handler);

		await sut.CheckForUpdateAsync(CancellationToken.None);

		_systemNotification.Verify(
			n => n.ShowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
			Times.Never);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenNewerVersionAvailable_ShowsNotification()
	{
		var json = """{"tag_name": "v2.0.0", "html_url": "https://github.com/vesnicancz/work-tracker/releases/tag/v2.0.0"}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		});
		var sut = CreateService("1.0.0", handler);

		await sut.CheckForUpdateAsync(CancellationToken.None);

		_systemNotification.Verify(
			n => n.ShowNotificationAsync(
				It.IsAny<string>(),
				It.IsAny<string>(),
				"https://github.com/vesnicancz/work-tracker/releases/tag/v2.0.0"),
			Times.Once);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenSameVersion_DoesNotNotify()
	{
		var json = """{"tag_name": "v1.0.0", "html_url": "https://example.com"}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		});
		var sut = CreateService("1.0.0", handler);

		await sut.CheckForUpdateAsync(CancellationToken.None);

		_systemNotification.Verify(
			n => n.ShowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
			Times.Never);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenOlderVersion_DoesNotNotify()
	{
		var json = """{"tag_name": "v0.9.0", "html_url": "https://example.com"}""";
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
		{
			Content = new StringContent(json, Encoding.UTF8, "application/json")
		});
		var sut = CreateService("1.0.0", handler);

		await sut.CheckForUpdateAsync(CancellationToken.None);

		_systemNotification.Verify(
			n => n.ShowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
			Times.Never);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenNetworkFails_DoesNotThrow()
	{
		var handler = new FakeHandler(_ => throw new HttpRequestException("Network error"));
		var sut = CreateService("1.0.0", handler);

		var act = () => sut.CheckForUpdateAsync(CancellationToken.None);

		await act.Should().NotThrowAsync();
		_systemNotification.Verify(
			n => n.ShowNotificationAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()),
			Times.Never);
	}

	[Fact]
	public async Task CheckForUpdateAsync_WhenApiReturns404_DoesNotThrow()
	{
		var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
		var sut = CreateService("1.0.0", handler);

		var act = () => sut.CheckForUpdateAsync(CancellationToken.None);

		await act.Should().NotThrowAsync();
	}

	[Theory]
	[InlineData("1.2.3-alpha.1+build123", "1.2.3")]
	[InlineData("v1.2.3", "1.2.3")]
	[InlineData("1.2.3+metadata", "1.2.3")]
	[InlineData("v2.0.0-rc.1", "2.0.0")]
	public void ParseVersion_HandlesVariousFormats(string input, string expected)
	{
		var result = UpdateCheckService.ParseVersion(input);

		result.Should().NotBeNull();
		result!.ToString(3).Should().Be(expected);
	}

	[Theory]
	[InlineData("")]
	[InlineData("not-a-version")]
	public void ParseVersion_ReturnsNull_ForInvalidInput(string input)
	{
		var result = UpdateCheckService.ParseVersion(input);

		result.Should().BeNull();
	}

	private sealed class FakeHandler : HttpMessageHandler
	{
		private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

		public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) => _handler = handler;

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(_handler(request));
		}
	}
}