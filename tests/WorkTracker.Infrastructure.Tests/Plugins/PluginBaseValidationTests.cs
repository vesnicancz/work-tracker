using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.Plugin.Abstractions;

namespace WorkTracker.Infrastructure.Tests.Plugins;

public class PluginBaseValidationTests
{
	private sealed class TestPlugin(params PluginConfigurationField[] fields) : PluginBase(NullLogger.Instance)
	{
		public override PluginMetadata Metadata { get; } = new()
		{
			Id = "test.plugin",
			Name = "Test Plugin",
			Version = new Version(1, 0),
			Author = "Test"
		};

		public override IReadOnlyList<PluginConfigurationField> GetConfigurationFields() => fields;
	}

	private static PluginConfigurationField Field(
		PluginConfigurationFieldType type = PluginConfigurationFieldType.Text,
		bool isRequired = false,
		string? pattern = null,
		string? message = null) => new()
	{
		Key = "Value",
		Label = "Value",
		Type = type,
		IsRequired = isRequired,
		ValidationPattern = pattern,
		ValidationMessage = message
	};

	private static Task<PluginValidationResult> ValidateAsync(PluginConfigurationField field, string? value)
	{
		var configuration = new Dictionary<string, string>();
		if (value != null)
		{
			configuration[field.Key] = value;
		}

		return new TestPlugin(field).ValidateConfigurationAsync(configuration, TestContext.Current.CancellationToken);
	}

	[Fact]
	public async Task RequiredField_Missing_Fails()
	{
		var result = await ValidateAsync(Field(isRequired: true), null);

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle().Which.Should().Contain("Required field 'Value'");
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public async Task OptionalField_Empty_SkipsPatternAndTypeChecks(string? value)
	{
		var result = await ValidateAsync(Field(PluginConfigurationFieldType.Url, pattern: "^never$"), value);

		result.IsValid.Should().BeTrue();
	}

	[Fact]
	public async Task OptionalField_WithValue_IsCheckedAgainstPattern()
	{
		var result = await ValidateAsync(Field(pattern: @"^#[0-9A-Fa-f]{6}$", message: "Must be a hex colour"), "red");

		result.IsValid.Should().BeFalse();
		result.Errors.Should().ContainSingle().Which.Should().Be("Must be a hex colour");
	}

	[Fact]
	public async Task OptionalField_MatchingPattern_Passes()
	{
		var result = await ValidateAsync(Field(pattern: @"^#[0-9A-Fa-f]{6}$"), "#FF0000");

		result.IsValid.Should().BeTrue();
	}

	[Theory]
	[InlineData(PluginConfigurationFieldType.Url, "https://example.com/api", true)]
	[InlineData(PluginConfigurationFieldType.Url, "http://localhost:8080", true)]
	[InlineData(PluginConfigurationFieldType.Url, "example.com", false)]
	[InlineData(PluginConfigurationFieldType.Url, "ftp://example.com", false)]
	[InlineData(PluginConfigurationFieldType.Email, "user@example.com", true)]
	[InlineData(PluginConfigurationFieldType.Email, "not an email", false)]
	[InlineData(PluginConfigurationFieldType.Number, "50", true)]
	[InlineData(PluginConfigurationFieldType.Number, "2.5", true)]
	[InlineData(PluginConfigurationFieldType.Number, "fifty", false)]
	[InlineData(PluginConfigurationFieldType.Checkbox, "True", true)]
	[InlineData(PluginConfigurationFieldType.Checkbox, "yes", false)]
	[InlineData(PluginConfigurationFieldType.Text, "anything at all", true)]
	[InlineData(PluginConfigurationFieldType.Password, "s3cr3t!", true)]
	[InlineData(PluginConfigurationFieldType.MultilineText, "line one\nline two", true)]
	public async Task FieldWithValue_IsCheckedAgainstItsType(PluginConfigurationFieldType type, string value, bool expectedValid)
	{
		var result = await ValidateAsync(Field(type), value);

		result.IsValid.Should().Be(expectedValid);
	}

	[Fact]
	public async Task FieldWithWrongType_ReportsTypeErrorBeforePattern()
	{
		var result = await ValidateAsync(Field(PluginConfigurationFieldType.Number, pattern: @"^\d+$", message: "Digits only"), "abc");

		result.Errors.Should().ContainSingle().Which.Should().Contain("must be a number");
	}
}
