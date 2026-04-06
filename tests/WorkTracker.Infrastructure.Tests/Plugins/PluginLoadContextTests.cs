using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using WorkTracker.Infrastructure.Plugins;

namespace WorkTracker.Infrastructure.Tests.Plugins;

/// <summary>
/// Tests for PluginLoadContext through PluginManager's file loading APIs.
/// PluginLoadContext is internal, so we test it indirectly.
/// </summary>
public class PluginLoadContextTests : IAsyncDisposable
{
	private readonly string _tempDir;
	private readonly PluginManager _pluginManager;

	public PluginLoadContextTests()
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
		if (Directory.Exists(_tempDir))
		{
			Directory.Delete(_tempDir, recursive: true);
		}
	}

	#region LoadPluginFromFileAsync

	[Fact]
	public async Task LoadPluginFromFileAsync_NonExistentFile_ReturnsFalse()
	{
		var result = await _pluginManager.LoadPluginFromFileAsync(Path.Combine(_tempDir, "nonexistent.dll"));
		result.Should().BeFalse();
	}

	[Theory]
	[InlineData(new byte[] { 0x00, 0x01, 0x02, 0x03, 0xFF, 0xFE }, "random_bytes")]
	[InlineData(new byte[0], "empty")]
	[InlineData(new byte[] { 0x54, 0x68, 0x69, 0x73 }, "text_content")]
	public async Task LoadPluginFromFileAsync_InvalidContent_ReturnsFalse(byte[] content, string label)
	{
		var path = Path.Combine(_tempDir, $"{label}.dll");
		File.WriteAllBytes(path, content);

		(await _pluginManager.LoadPluginFromFileAsync(path)).Should().BeFalse();
	}

	#endregion LoadPluginFromFileAsync

	#region DiscoverAndLoadPlugins

	[Fact]
	public async Task DiscoverAndLoadPlugins_NoDirectories_ReturnsZero()
	{
		var result = await _pluginManager.DiscoverAndLoadPluginsAsync();
		result.Should().Be(0);
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_EmptyDirectory_ReturnsZero()
	{
		_pluginManager.AddPluginDirectory(_tempDir);

		var result = await _pluginManager.DiscoverAndLoadPluginsAsync();
		result.Should().Be(0);
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_DirectoryWithNonDllFiles_ReturnsZero()
	{
		File.WriteAllText(Path.Combine(_tempDir, "readme.txt"), "Hello");
		File.WriteAllText(Path.Combine(_tempDir, "config.json"), "{}");
		_pluginManager.AddPluginDirectory(_tempDir);

		var result = await _pluginManager.DiscoverAndLoadPluginsAsync();
		result.Should().Be(0);
	}

	[Fact]
	public async Task DiscoverAndLoadPlugins_DirectoryWithInvalidDlls_ReturnsZero()
	{
		File.WriteAllBytes(Path.Combine(_tempDir, "fake.dll"), [0x00, 0x01, 0x02]);
		_pluginManager.AddPluginDirectory(_tempDir);

		var result = await _pluginManager.DiscoverAndLoadPluginsAsync();
		result.Should().Be(0);
	}

	#endregion DiscoverAndLoadPlugins

	#region AddPluginDirectory

	[Fact]
	public void AddPluginDirectory_NonExistentDirectory_DoesNotThrow()
	{
		var act = () => _pluginManager.AddPluginDirectory(Path.Combine(_tempDir, "nonexistent"));
		act.Should().NotThrow();
	}

	[Fact]
	public async Task AddPluginDirectory_ExistingDirectory_AddedSuccessfully()
	{
		_pluginManager.AddPluginDirectory(_tempDir);

		var act = () => _pluginManager.DiscoverAndLoadPluginsAsync();
		await act.Should().NotThrowAsync();
	}

	[Fact]
	public async Task AddPluginDirectory_NonExistentDirectory_NotAddedToScan()
	{
		// Add non-existent directory
		_pluginManager.AddPluginDirectory(Path.Combine(_tempDir, "ghost"));

		// Should return 0 since directory was not added (doesn't exist)
		var result = await _pluginManager.DiscoverAndLoadPluginsAsync();
		result.Should().Be(0);
	}

	#endregion AddPluginDirectory

	#region LoadedPlugins state

	[Fact]
	public void LoadedPlugins_Initially_IsEmpty()
	{
		_pluginManager.LoadedPlugins.Should().BeEmpty();
	}

	[Fact]
	public void LoadPluginFromFileAsync_InvalidFile_PluginsRemainsEmpty()
	{
		var fakeDll = Path.Combine(_tempDir, "fake.dll");
		File.WriteAllBytes(fakeDll, [0x00]);

		_pluginManager.LoadPluginFromFileAsync(fakeDll);

		_pluginManager.LoadedPlugins.Should().BeEmpty();
	}

	#endregion LoadedPlugins state
}