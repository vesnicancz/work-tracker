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

	private void SetPlugins(IRemoteControlPlugin[] all, IRemoteControlPlugin[] enabled)
	{
		_pluginManager.Setup(p => p.AllRemoteControlPlugins).Returns(all);
		_pluginManager.Setup(p => p.RemoteControlPlugins).Returns(enabled);
	}

	[Fact]
	public async Task Sync_AttachesEnabledAndDetachesDisabled()
	{
		var enabled = new Mock<IRemoteControlPlugin>();
		var disabled = new Mock<IRemoteControlPlugin>();
		SetPlugins([enabled.Object, disabled.Object], [enabled.Object]);

		await _coordinator.SyncAsync(TestToken);

		enabled.Verify(p => p.AttachAsync(_host, It.IsAny<CancellationToken>()), Times.Once);
		enabled.Verify(p => p.DetachAsync(), Times.Never);
		disabled.Verify(p => p.DetachAsync(), Times.Once);
		disabled.Verify(p => p.AttachAsync(It.IsAny<IRemoteControlHost>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Sync_FailingPlugin_DoesNotStopTheOthers()
	{
		var broken = new Mock<IRemoteControlPlugin>();
		broken.Setup(p => p.Metadata).Returns(new PluginMetadata { Id = "broken", Name = "Broken", Version = new Version(1, 0), Author = "Test" });
		broken.Setup(p => p.AttachAsync(It.IsAny<IRemoteControlHost>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException());
		var working = new Mock<IRemoteControlPlugin>();
		SetPlugins([broken.Object, working.Object], [broken.Object, working.Object]);

		await _coordinator.Invoking(c => c.SyncAsync(TestToken)).Should().NotThrowAsync();

		working.Verify(p => p.AttachAsync(_host, It.IsAny<CancellationToken>()), Times.Once);
	}
}
