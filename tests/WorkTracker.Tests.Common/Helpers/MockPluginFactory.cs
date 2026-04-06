using Moq;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Tests.Common.Helpers;

/// <summary>
/// Shared factory for creating mock plugin instances in tests.
/// Covers common setup patterns for IPlugin, IWorklogUploadPlugin,
/// IWorkSuggestionPlugin, and ITestablePlugin.
/// </summary>
public static class MockPluginFactory
{
	public static PluginMetadata CreateMetadata(
		string id = "test.plugin",
		string name = "Test Plugin",
		string author = "Test",
		string? description = null,
		Version? version = null) => new()
	{
		Id = id,
		Name = name,
		Version = version ?? new Version(1, 0),
		Author = author,
		Description = description
	};

	public static Mock<IPlugin> CreatePlugin(
		string id = "test.plugin",
		string name = "Test Plugin",
		params PluginConfigurationField[] fields)
	{
		var mock = new Mock<IPlugin>();
		mock.Setup(p => p.Metadata).Returns(CreateMetadata(id, name));
		mock.Setup(p => p.GetConfigurationFields()).Returns(fields);
		return mock;
	}

	public static Mock<IWorklogUploadPlugin> CreateWorklogPlugin(
		string id = "test.plugin",
		string name = "Test Plugin",
		params PluginConfigurationField[] fields)
	{
		var mock = new Mock<IWorklogUploadPlugin>();
		mock.Setup(p => p.Metadata).Returns(CreateMetadata(id, name));
		mock.Setup(p => p.GetConfigurationFields()).Returns(fields);
		mock.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		return mock;
	}

	public static Mock<IWorkSuggestionPlugin> CreateSuggestionPlugin(
		string id = "test.plugin",
		string name = "Test Plugin",
		bool supportsSearch = false)
	{
		var mock = new Mock<IWorkSuggestionPlugin>();
		mock.Setup(p => p.Metadata).Returns(CreateMetadata(id, name));
		mock.Setup(p => p.GetConfigurationFields()).Returns([]);
		mock.Setup(p => p.SupportsSearch).Returns(supportsSearch);
		mock.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		return mock;
	}

	public static Mock<ITestablePlugin> CreateTestablePlugin(
		string id = "test.plugin",
		string name = "Test Plugin")
	{
		var mock = new Mock<ITestablePlugin>();
		mock.Setup(p => p.Metadata).Returns(CreateMetadata(id, name));
		mock.Setup(p => p.GetConfigurationFields()).Returns([]);
		mock.Setup(p => p.InitializeAsync(It.IsAny<IDictionary<string, string>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(true);
		return mock;
	}
}
