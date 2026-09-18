using FluentAssertions;
using WorkTracker.Avalonia.Services.Linux;

namespace WorkTracker.Avalonia.Tests.Services;

/// <summary>
/// The portal answers a call on an object path the caller has to predict in order to subscribe
/// before making it, so getting these strings wrong means every portal call hangs until it times
/// out rather than failing visibly.
/// </summary>
public class PortalHandlesTests
{
	[Theory]
	[InlineData(":1.42", "1_42")]
	[InlineData(":1.2345", "1_2345")]
	[InlineData(":1.10.3", "1_10_3")]
	public void SenderToken_DropsTheColonAndReplacesTheDots(string uniqueName, string expected)
	{
		PortalHandles.SenderToken(uniqueName).Should().Be(expected);
	}

	/// <summary>A name that already lacks the colon must not lose its first character.</summary>
	[Fact]
	public void SenderToken_LeavesANameWithoutALeadingColonIntact()
	{
		PortalHandles.SenderToken("1.42").Should().Be("1_42");
	}

	[Fact]
	public void RequestPath_BuildsThePathThePortalWillAnswerOn()
	{
		PortalHandles.RequestPath(":1.42", "wtdeadbeef")
			.Should().Be("/org/freedesktop/portal/desktop/request/1_42/wtdeadbeef");
	}

	/// <summary>
	/// A token becomes the last element of an object path, where only [A-Za-z0-9_] is legal and an
	/// element may not start with a digit.
	/// </summary>
	[Fact]
	public void NewToken_IsUsableAsAnObjectPathElement()
	{
		var token = PortalHandles.NewToken();

		token.Should().MatchRegex("^[A-Za-z_][A-Za-z0-9_]*$");
	}

	[Fact]
	public void NewToken_DiffersBetweenCalls()
	{
		var tokens = Enumerable.Range(0, 50).Select(_ => PortalHandles.NewToken()).ToList();

		tokens.Should().OnlyHaveUniqueItems();
	}

	[Theory]
	[InlineData("")]
	[InlineData(null)]
	public void SenderToken_RejectsAnEmptyName(string? uniqueName)
	{
		var act = () => PortalHandles.SenderToken(uniqueName!);

		act.Should().Throw<ArgumentException>();
	}
}
