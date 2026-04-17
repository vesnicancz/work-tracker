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

	[Fact]
	public void IsCheckbox_WhenTypeCheckbox_ReturnsTrue()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));

		vm.IsCheckbox.Should().BeTrue();
		vm.IsTextInput.Should().BeFalse();
	}

	[Theory]
	[InlineData(PluginConfigurationFieldType.Text)]
	[InlineData(PluginConfigurationFieldType.Password)]
	[InlineData(PluginConfigurationFieldType.Url)]
	[InlineData(PluginConfigurationFieldType.Number)]
	[InlineData(PluginConfigurationFieldType.Email)]
	[InlineData(PluginConfigurationFieldType.MultilineText)]
	[InlineData(PluginConfigurationFieldType.Dropdown)]
	public void IsTextInput_WhenTypeNotCheckbox_ReturnsTrue(PluginConfigurationFieldType type)
	{
		var (vm, _) = CreateVm(CreateField(type: type));

		vm.IsTextInput.Should().BeTrue();
		vm.IsCheckbox.Should().BeFalse();
	}

	[Theory]
	[InlineData("true", true)]
	[InlineData("True", true)]
	[InlineData("TRUE", true)]
	[InlineData("false", false)]
	[InlineData("False", false)]
	[InlineData("", false)]
	[InlineData("not-a-bool", false)]
	public void BoolValue_ParsesStoredValue(string stored, bool expected)
	{
		var (vm, plugin) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		plugin.Configuration["TestKey"] = stored;

		vm.BoolValue.Should().Be(expected);
	}

	[Fact]
	public void BoolValue_WhenKeyMissing_ReturnsFalse()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));

		vm.BoolValue.Should().BeFalse();
	}

	[Fact]
	public void BoolValue_SetTrue_WritesTrueString()
	{
		var (vm, plugin) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));

		vm.BoolValue = true;

		plugin.Configuration["TestKey"].Should().Be("true");
	}

	[Fact]
	public void BoolValue_SetFalse_WritesFalseString()
	{
		var (vm, plugin) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		plugin.Configuration["TestKey"] = "true";

		vm.BoolValue = false;

		plugin.Configuration["TestKey"].Should().Be("false");
	}

	[Fact]
	public void BoolValue_Set_RaisesBoolValueAndValuePropertyChanged()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		var raised = new List<string?>();
		vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

		vm.BoolValue = true;

		raised.Should().Contain(nameof(ConfigurationFieldViewModel.Value));
		raised.Should().Contain(nameof(ConfigurationFieldViewModel.BoolValue));
	}

	[Fact]
	public void BoolValue_SetSameValue_DoesNotRaisePropertyChanged()
	{
		var (vm, plugin) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		plugin.Configuration["TestKey"] = "true";
		var raised = false;
		vm.PropertyChanged += (_, _) => raised = true;

		vm.BoolValue = true;

		raised.Should().BeFalse();
	}

	[Fact]
	public void Value_Set_RaisesBoolValuePropertyChanged()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(ConfigurationFieldViewModel.BoolValue))
			{
				raised = true;
			}
		};

		vm.Value = "true";

		raised.Should().BeTrue();
	}

	[Fact]
	public void RefreshValue_RaisesBoolValuePropertyChanged()
	{
		var (vm, _) = CreateVm(CreateField(type: PluginConfigurationFieldType.Checkbox));
		var raised = false;
		vm.PropertyChanged += (_, e) =>
		{
			if (e.PropertyName == nameof(ConfigurationFieldViewModel.BoolValue))
			{
				raised = true;
			}
		};

		vm.RefreshValue();

		raised.Should().BeTrue();
	}
}
