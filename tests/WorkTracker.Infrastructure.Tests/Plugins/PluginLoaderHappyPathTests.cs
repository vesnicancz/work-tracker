using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Infrastructure.Plugins;

namespace WorkTracker.Infrastructure.Tests.Plugins;

/// <summary>
/// Positive-path tests for plugin discovery and loading. A real plugin assembly
/// (WorkTracker.Plugin.Luxafor, present in the test output via a test-only project
/// reference) is copied into a temp directory and loaded through PluginManager.
/// </summary>
public sealed class PluginLoaderHappyPathTests : IAsyncDisposable
{
	private const string PluginFileName = "WorkTracker.Plugin.Luxafor.dll";

	private readonly string _tempDir;
	private readonly PluginManager _pluginManager;

	public PluginLoaderHappyPathTests()
	{
		_tempDir = Path.Combine(Path.GetTempPath(), $"wt_test_{Guid.NewGuid():N}");
		Directory.CreateDirectory(_tempDir);
		var mockLoggerFactory = new Mock<ILoggerFactory>();
		mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(Mock.Of<ILogger>());
		_pluginManager = new PluginManager(mockLoggerFactory.Object, Mock.Of<IHttpClientFactory>());
	}

	public async ValueTask DisposeAsync()
	{
		await _pluginManager.DisposeAsync();

		// Unloading a collectible AssemblyLoadContext only releases the DLL file lock after
		// the context is garbage collected, so deletion needs GC nudges and is best-effort.
		for (var attempt = 0; attempt < 5 && Directory.Exists(_tempDir); attempt++)
		{
			try
			{
				Directory.Delete(_tempDir, recursive: true);
			}
			catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
	}

	private string CopyRealPluginTo(string targetFileName)
	{
		var source = Path.Combine(AppContext.BaseDirectory, PluginFileName);
		var target = Path.Combine(_tempDir, targetFileName);
		File.Copy(source, target);
		return target;
	}

	[Fact]
	public async Task LoadPluginFromFileAsync_RealPluginAssembly_LoadsAndRegisters()
	{
		var path = CopyRealPluginTo(PluginFileName);

		var loaded = await _pluginManager.LoadPluginFromFileAsync(path);

		loaded.Should().BeTrue();
		_pluginManager.LoadedPlugins.Should().ContainSingle();
	}

	[Fact]
	public async Task LoadPluginFromFileAsync_SamePluginTwice_SecondLoadIsRejected()
	{
		var path = CopyRealPluginTo(PluginFileName);
		await _pluginManager.LoadPluginFromFileAsync(path);

		// Same assembly again from a different directory — same plugin id must be rejected
		var secondDir = Path.Combine(_tempDir, "second");
		Directory.CreateDirectory(secondDir);
		var secondCopy = Path.Combine(secondDir, PluginFileName);
		File.Copy(path, secondCopy);
		var loadedAgain = await _pluginManager.LoadPluginFromFileAsync(secondCopy);

		loadedAgain.Should().BeFalse("the same plugin id is already registered");
		_pluginManager.LoadedPlugins.Should().ContainSingle();
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_RealPluginWithProperPrefix_IsLoaded()
	{
		CopyRealPluginTo(PluginFileName);
		_pluginManager.AddPluginDirectory(_tempDir);

		var count = await _pluginManager.DiscoverAndLoadPluginsAsync();

		count.Should().Be(1);
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_AssemblyWithoutPluginPrefix_IsIgnored()
	{
		CopyRealPluginTo("Renamed.Luxafor.dll");
		_pluginManager.AddPluginDirectory(_tempDir);

		var count = await _pluginManager.DiscoverAndLoadPluginsAsync();

		count.Should().Be(0);
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_AbstractionsAssembly_IsIgnored()
	{
		File.Copy(
			Path.Combine(AppContext.BaseDirectory, "WorkTracker.Plugin.Abstractions.dll"),
			Path.Combine(_tempDir, "WorkTracker.Plugin.Abstractions.dll"));
		_pluginManager.AddPluginDirectory(_tempDir);

		var count = await _pluginManager.DiscoverAndLoadPluginsAsync();

		count.Should().Be(0);
	}
}
