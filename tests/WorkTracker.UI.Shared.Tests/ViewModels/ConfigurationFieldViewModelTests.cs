using FluentAssertions;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Tests.Common.Helpers;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.UI.Shared.Tests.ViewModels;

public class ConfigurationFieldViewModelTests
{
	private static PluginConfigurationField CreateField(
		string key = "TestKey",
		string label = "Test Label",
		string? defaultValue = null,
		PluginConfigurationFieldType type = PluginConfigurationFieldType.Text,
		bool isRequired = false) => new()
	{
		Key = key,
		Label = label,
		DefaultValue = defaultValue,
		Type = type,
		IsRequired = isRequired
	};

	private static (ConfigurationFieldViewModel Vm, PluginViewModel Plugin) CreateVm(
		PluginConfigurationField? field = null)
	{
		var pluginVm = new PluginViewModel(MockPluginFactory.CreatePlugin(id: "test-plugin").Object);
		var f = field ?? CreateField();
		var vm = new ConfigurationFieldViewModel(f, pluginVm);
		return (vm, pluginVm);
	}

	[Fact]
	public void Key_ReturnsFieldKey()
	{
		var (vm, _) = CreateVm(CreateField(key: "ApiToken"));

		vm.Key.Should().Be("ApiToken");
	}

	[Fact]
	public void Label_ReturnsFieldLabel()
	{
		var (vm, _) = CreateVm(CreateField(label: "API Token"));

		vm.Label.Should().Be("API Token");
	}

	[Fact]
	public void Type_ReturnsFieldType()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Password));

		vm.Type.Should().Be(PluginConfigurationFieldType.Password);
	}

	[Fact]
	public void IsRequired_ReturnsFieldIsRequired()
	{
		var (vm, _) = CreateVm(CreateField(isRequired: true));

		vm.IsRequired.Should().BeTrue();
	}

	[Fact]
	public void Value_WhenKeyNotInDictionary_ReturnsEmpty()
	{
		var (vm, _) = CreateVm();

		vm.Value.Should().BeEmpty();
	}

	[Fact]
	public void Value_WhenKeyInDictionary_ReturnsValue()
	{
		var (vm, plugin) = CreateVm();
		plugin.Configuration["TestKey"] = "my-value";

		vm.Value.Should().Be("my-value");
	}

	[Fact]
	public void Value_Set_UpdatesDictionary()
	{
		var (vm, plugin) = CreateVm();

		vm.Value = "new-value";

		plugin.Configuration["TestKey"].Should().Be("new-value");
	}

	[Fact]
	public void Value_Set_RaisesPropertyChanged()
	{
		var (vm, _) = CreateVm();
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(ConfigurationFieldViewModel.Value))
			{
				raised = true;
			}
		};

		vm.Value = "changed";

		raised.Should().BeTrue();
	}

	[Fact]
	public void Value_SetSameValue_DoesNotRaisePropertyChanged()
	{
		var (vm, plugin) = CreateVm();
		plugin.Configuration["TestKey"] = "existing";
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(ConfigurationFieldViewModel.Value))
			{
				raised = true;
			}
		};

		vm.Value = "existing";

		raised.Should().BeFalse();
	}

	[Fact]
	public void RefreshValue_RaisesPropertyChanged()
	{
		var (vm, _) = CreateVm();
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(ConfigurationFieldViewModel.Value))
			{
				raised = true;
			}
		};

		vm.RefreshValue();

		raised.Should().BeTrue();
	}
}
