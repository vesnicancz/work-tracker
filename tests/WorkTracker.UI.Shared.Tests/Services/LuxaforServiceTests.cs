using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using WorkTracker.UI.Shared.Services;

namespace WorkTracker.UI.Shared.Tests.Services;

public class LuxaforServiceTests : IDisposable
{
	private readonly LuxaforService _sut;

	public LuxaforServiceTests()
	{
		_sut = new LuxaforService(NullLogger<LuxaforService>.Instance);
	}

	public void Dispose()
	{
		_sut.Dispose();
	}

	[Fact]
	public void IsDeviceConnected_WhenNoDevice_ReturnsFalse()
	{
		_sut.IsDeviceConnected.Should().BeFalse();
	}

	[Fact]
	public void SetColor_WhenNoDevice_DoesNotThrow()
	{
		var act = () => _sut.SetColor(255, 0, 0);
		act.Should().NotThrow();
	}

	[Fact]
	public void TurnOff_WhenNoDevice_DoesNotThrow()
	{
		var act = () => _sut.TurnOff();
		act.Should().NotThrow();
	}

	[Fact]
	public void SetColor_AfterDispose_DoesNotThrow()
	{
		_sut.Dispose();
		var act = () => _sut.SetColor(255, 0, 0);
		act.Should().NotThrow();
	}
}
