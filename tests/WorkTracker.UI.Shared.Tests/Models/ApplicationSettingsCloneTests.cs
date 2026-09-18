using System.Reflection;
using FluentAssertions;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.UI.Shared.Models;

namespace WorkTracker.UI.Shared.Tests.Models;

/// <summary>
/// <see cref="ApplicationSettings.Clone"/> is what stops a partial save (the Settings dialog does
/// not own every setting) from resetting the settings it does not know about. These tests fail
/// when a new property is added to <see cref="ApplicationSettings"/> but not to Clone.
/// </summary>
public class ApplicationSettingsCloneTests
{
	private static ApplicationSettings NonDefaultSettings() => new()
	{
		LastSubmissionMode = WorklogSubmissionMode.Aggregated,
		LastSubmissionProviderId = "tempo",
		SubmissionModeByProvider = new Dictionary<string, WorklogSubmissionMode>
		{
			["tempo"] = WorklogSubmissionMode.Timed
		},
		CloseWindowBehavior = CloseWindowBehavior.ExitApplication,
		StartWithWindows = true,
		StartMinimized = true,
		CheckForUpdates = false,
		PluginConfigurations = new Dictionary<string, Dictionary<string, string>>
		{
			["tempo"] = new() { ["Url"] = "https://example.invalid" }
		},
		EnabledPlugins = new Dictionary<string, bool> { ["tempo"] = true },
		FavoriteWorkItems =
		[
			new FavoriteWorkItem { Name = "Standup", TicketId = "PROJ-1", Description = "Daily", ShowAsTemplate = true }
		],
		Theme = "Synthwave",
		FollowSystemTheme = true,
		LightTheme = "Sandstone",
		DarkTheme = "Abyss",
		Language = "cs",
		Pomodoro = new PomodoroSettings
		{
			Enabled = true,
			WorkMinutes = 50,
			ShortBreakMinutes = 7,
			LongBreakMinutes = 20,
			PomodorosBeforeLongBreak = 3,
			AutoStartWorkTracking = true,
			AutoStopWorkTracking = true
		}
	};

	[Fact]
	public void Clone_CopiesEveryProperty()
	{
		var original = NonDefaultSettings();
		var fresh = new ApplicationSettings();

		var clone = original.Clone();

		// Reflection rather than a hand-written list: a property added to ApplicationSettings and
		// forgotten in Clone shows up here as the default value instead of the original's.
		foreach (var property in typeof(ApplicationSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
		{
			if (!property.CanWrite)
			{
				continue;
			}

			var expected = property.GetValue(original);
			var defaultValue = property.GetValue(fresh);

			property.GetValue(clone).Should().BeEquivalentTo(expected,
				$"Clone() must copy {property.Name}");

			// Guards the test itself: a property left at its default here would pass vacuously.
			expected.Should().NotBeEquivalentTo(defaultValue,
				$"NonDefaultSettings() must give {property.Name} a non-default value for this test to mean anything");
		}
	}

	[Fact]
	public void Clone_DoesNotShareMutableState()
	{
		var original = NonDefaultSettings();

		var clone = original.Clone();
		clone.Pomodoro.WorkMinutes = 15;
		clone.FavoriteWorkItems[0].Name = "Renamed";
		clone.FavoriteWorkItems.Add(new FavoriteWorkItem { Name = "Extra" });
		clone.PluginConfigurations["tempo"]["Url"] = "https://changed.invalid";
		clone.EnabledPlugins["tempo"] = false;
		clone.SubmissionModeByProvider["tempo"] = WorklogSubmissionMode.Aggregated;

		original.Pomodoro.WorkMinutes.Should().Be(50);
		original.FavoriteWorkItems.Should().ContainSingle().Which.Name.Should().Be("Standup");
		original.PluginConfigurations["tempo"]["Url"].Should().Be("https://example.invalid");
		original.EnabledPlugins["tempo"].Should().BeTrue();
		original.SubmissionModeByProvider["tempo"].Should().Be(WorklogSubmissionMode.Timed);
	}
}
