using FluentAssertions;
using Moq;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Helpers;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class PluginViewModelTests
{
	// --- Metadata properties ---

	[Fact]
	public void Name_ReturnsPluginName()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin(name: "Test Plugin").Object);

		vm.Name.Should().Be("Test Plugin");
	}

	[Fact]
	public void Version_ReturnsPluginVersion()
	{
		var mock = MockPluginFactory.CreatePlugin();
		mock.Setup(p => p.Metadata).Returns(MockPluginFactory.CreateMetadata(version: new Version(2, 1, 0)));
		var vm = new PluginViewModel(mock.Object);

		vm.Version.Should().Be("2.1.0");
	}

	[Fact]
	public void Author_ReturnsPluginAuthor()
	{
		var mock = MockPluginFactory.CreatePlugin();
		mock.Setup(p => p.Metadata).Returns(MockPluginFactory.CreateMetadata(author: "Test Author"));
		var vm = new PluginViewModel(mock.Object);

		vm.Author.Should().Be("Test Author");
	}

	[Fact]
	public void Description_ReturnsPluginDescription()
	{
		var mock = MockPluginFactory.CreatePlugin();
		mock.Setup(p => p.Metadata).Returns(MockPluginFactory.CreateMetadata(description: "A test plugin"));
		var vm = new PluginViewModel(mock.Object);

		vm.Description.Should().Be("A test plugin");
	}

	[Fact]
	public void Description_WhenNull_ReturnsEmpty()
	{
		var mock = MockPluginFactory.CreatePlugin();
		mock.Setup(p => p.Metadata).Returns(MockPluginFactory.CreateMetadata(description: null));
		var vm = new PluginViewModel(mock.Object);

		vm.Description.Should().BeEmpty();
	}

	// --- Configuration fields ---

	[Fact]
	public void Constructor_CreatesFieldViewModels()
	{
		var fields = new[]
		{
			new PluginConfigurationField { Key = "url", Label = "URL" },
			new PluginConfigurationField { Key = "token", Label = "Token" }
		};
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin(fields: fields).Object);

		vm.ConfigurationFields.Should().HaveCount(2);
		vm.ConfigurationFields[0].Key.Should().Be("url");
		vm.ConfigurationFields[1].Key.Should().Be("token");
	}

	[Fact]
	public void Constructor_AppliesDefaultValues()
	{
		var fields = new[]
		{
			new PluginConfigurationField { Key = "baseUrl", Label = "Base URL", DefaultValue = "https://api.example.com" },
			new PluginConfigurationField { Key = "token", Label = "Token" }
		};
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin(fields: fields).Object);

		vm.Configuration["baseUrl"].Should().Be("https://api.example.com");
		vm.Configuration.Should().NotContainKey("token");
	}

	[Fact]
	public void Constructor_NoFields_CreatesEmptyCollections()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin().Object);

		vm.ConfigurationFields.Should().BeEmpty();
		vm.Configuration.Should().BeEmpty();
	}

	// --- SupportsTestConnection ---

	[Fact]
	public void SupportsTestConnection_NonTestablePlugin_ReturnsFalse()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin().Object);

		vm.SupportsTestConnection.Should().BeFalse();
	}

	[Fact]
	public void SupportsTestConnection_TestablePlugin_ReturnsTrue()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreateTestablePlugin().Object);

		vm.SupportsTestConnection.Should().BeTrue();
	}

	// --- IsEnabled ---

	[Fact]
	public void IsEnabled_DefaultsFalse()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin().Object);

		vm.IsEnabled.Should().BeFalse();
	}

	[Fact]
	public void IsEnabled_Set_RaisesPropertyChanged()
	{
		var vm = new PluginViewModel(MockPluginFactory.CreatePlugin().Object);
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(PluginViewModel.IsEnabled))
			{
				raised = true;
			}
		};

		vm.IsEnabled = true;

		raised.Should().BeTrue();
		vm.IsEnabled.Should().BeTrue();
	}

	// --- Plugin reference ---

	[Fact]
	public void Plugin_ReturnsOriginalPlugin()
	{
		var plugin = MockPluginFactory.CreatePlugin().Object;
		var vm = new PluginViewModel(plugin);

		vm.Plugin.Should().BeSameAs(plugin);
	}
}
