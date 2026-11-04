using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public class RemoteControlCoordinatorTests
{
	private readonly Mock<IPluginManager> _pluginManager = new();
	private readonly IRemoteControlHost _host = new Mock<IRemoteControlHost>().Object;
	private readonly RemoteControlCoordinator _coordinator;

	private static CancellationToken TestToken => TestContext.Current.CancellationToken;

	public RemoteControlCoordinatorTests()
	{
		_coordinator = new RemoteControlCoordinator(_pluginManager.Object, _host, NullLogger<RemoteControlCoordinator>.Instance);
	}

	[Fact]
	public async Task Sync_AttachesWhatIsRunning()
	{
		var running = new Mock<IRemoteControlPlugin>();
		_pluginManager.Setup(p => p.RemoteControlPlugins).Returns([running.Object]);

		await _coordinator.SyncAsync(TestToken);

		running.Verify(p => p.AttachAsync(_host, It.IsAny<CancellationToken>()), Times.Once);
	}

	[Fact]
	public async Task Sync_FailingPlugin_DoesNotStopTheOthers()
	{
		var broken = new Mock<IRemoteControlPlugin>();
		broken.Setup(p => p.Metadata).Returns(new PluginMetadata { Id = "broken", Name = "Broken", Version = new Version(1, 0), Author = "Test" });
		broken.Setup(p => p.AttachAsync(It.IsAny<IRemoteControlHost>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
		var working = new Mock<IRemoteControlPlugin>();
		_pluginManager.Setup(p => p.RemoteControlPlugins).Returns([broken.Object, working.Object]);

		await _coordinator.Invoking(c => c.SyncAsync(TestToken)).Should().NotThrowAsync();

		working.Verify(p => p.AttachAsync(_host, It.IsAny<CancellationToken>()), Times.Once);
	}
}
