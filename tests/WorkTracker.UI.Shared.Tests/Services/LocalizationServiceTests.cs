using System.Globalization;
using FluentAssertions;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

[Collection("LocalizationTests")]
public class LocalizationServiceTests : IDisposable
{
	private readonly LocalizationService _sut = new();
	private readonly CultureInfo _originalCulture = CultureInfo.CurrentCulture;
	private readonly CultureInfo _originalUICulture = CultureInfo.CurrentUICulture;
	private readonly LocalizationService? _originalInstance = LocalizationService.Instance;

	public void Dispose()
	{
		CultureInfo.CurrentCulture = _originalCulture;
		CultureInfo.CurrentUICulture = _originalUICulture;
		LocalizationService.SetInstance(_originalInstance ?? new LocalizationService());
	}

	#region GetString

	[Fact]
	public void GetString_ExistingKey_ReturnsLocalizedValue()
	{
		var result = _sut.GetString("AppTitle");

		result.Should().NotStartWith("[");
		result.Should().NotBeNullOrEmpty();
	}

	[Fact]
	public void GetString_NonExistentKey_ReturnsBracketedKey()
	{
		var result = _sut.GetString("NonExistentKey_12345");

		result.Should().Be("[NonExistentKey_12345]");
	}

	[Fact]
	public void Indexer_ReturnsGetString()
	{
		var viaGetString = _sut.GetString("AppTitle");
		var viaIndexer = _sut["AppTitle"];

		viaIndexer.Should().Be(viaGetString);
	}

	[Fact]
	public void Indexer_NonExistentKey_ReturnsBracketedKey()
	{
		var result = _sut["DoesNotExist_XYZ"];

		result.Should().Be("[DoesNotExist_XYZ]");
	}

	#endregion

	#region GetFormattedString

	[Fact]
	public void GetFormattedString_NonExistentKey_ReturnsBracketedKey()
	{
		var result = _sut.GetFormattedString("NonExistentKey_FMT", "arg1");

		result.Should().Be("[NonExistentKey_FMT]");
	}

	#endregion

	#region Culture switching

	[Fact]
	public void CurrentCulture_Default_MatchesSystemCulture()
	{
		_sut.CurrentCulture.Should().Be(CultureInfo.CurrentUICulture);
	}

	[Fact]
	public void CurrentCulture_Changed_RaisesPropertyChanged()
	{
		var propertyNames = new List<string>();
		_sut.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName!);

		_sut.CurrentCulture = new CultureInfo("cs");

		propertyNames.Should().Contain(nameof(LocalizationService.CurrentCulture));
	}

	[Fact]
	public void CurrentCulture_ChangedToSameValue_DoesNotRaiseEvent()
	{
		var current = _sut.CurrentCulture;
		var raised = false;
		_sut.PropertyChanged += (_, _) => raised = true;

		_sut.CurrentCulture = current;

		raised.Should().BeFalse();
	}

	[Fact]
	public void CurrentCulture_Changed_RaisesEmptyPropertyChangedForLanguageRefresh()
	{
		var propertyNames = new List<string?>();
		_sut.PropertyChanged += (_, e) => propertyNames.Add(e.PropertyName);

		_sut.CurrentCulture = new CultureInfo("cs");

		// Empty PropertyName signals all bindings to refresh
		propertyNames.Should().Contain(string.Empty);
	}

	[Fact]
	public void CurrentCulture_ChangeToCzech_AffectsTranslations()
	{
		_sut.CurrentCulture = new CultureInfo("cs");
		var czech = _sut.GetString("AppTitle");

		_sut.CurrentCulture = new CultureInfo("en");
		var english = _sut.GetString("AppTitle");

		czech.Should().NotStartWith("[");
		english.Should().NotStartWith("[");
	}

	#endregion

	#region AvailableCultures

	[Fact]
	public void AvailableCultures_ContainsEnglishAndCzech()
	{
		var cultures = _sut.AvailableCultures.ToList();

		cultures.Should().HaveCount(2);
		cultures.Should().Contain(c => c.TwoLetterISOLanguageName == "en");
		cultures.Should().Contain(c => c.TwoLetterISOLanguageName == "cs");
	}

	#endregion

	#region SetInstance

	[Fact]
	public void SetInstance_SetsStaticInstance()
	{
		var instance = new LocalizationService();
		LocalizationService.SetInstance(instance);

		LocalizationService.Instance.Should().BeSameAs(instance);
	}

	#endregion
}
