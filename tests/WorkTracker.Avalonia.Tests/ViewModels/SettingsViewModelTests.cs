using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.UI.Shared.Models;
using WorkTracker.UI.Shared.Orchestrators;
using WorkTracker.UI.Shared.Services;
using WorkTracker.UI.Shared.ViewModels;

namespace WorkTracker.Avalonia.Tests.ViewModels;

public class SettingsViewModelTests
{
	private sealed class Harness
	{
		public Mock<ISettingsOrchestrator> Orchestrator { get; } = new();
		public Mock<ISettingsService> Settings { get; } = new();
		public Mock<IAutostartManager> Autostart { get; } = new();
		public Mock<ILocalizationService> Localization { get; } = new();
		public Mock<IThemeService> Theme { get; } = new();
		public ApplicationSettings SettingsModel { get; } = new();

		public Harness()
		{
			Settings.SetupGet(s => s.Settings).Returns(SettingsModel);
			Orchestrator.Setup(o => o.LoadPlugins()).Returns([]);
			Localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
		}

		public SettingsViewModel CreateViewModel()
		{
			return new SettingsViewModel(
				Orchestrator.Object,
				Settings.Object,
				NullLogger<SettingsViewModel>.Instance,
				Autostart.Object,
				Localization.Object,
				Theme.Object);
		}
	}

	private static FavoriteWorkItem Favorite(string name) => new() { Name = name };

	#region Language

	[Fact]
	public void Constructor_SeedsSelectedLanguageFromSettings()
	{
		var harness = new Harness();
		harness.SettingsModel.Language = "cs";

		var vm = harness.CreateViewModel();

		vm.SelectedLanguage.Code.Should().Be("cs");
	}

	[Fact]
	public void Constructor_UnknownStoredLanguage_FallsBackToSystem()
	{
		var harness = new Harness();
		harness.SettingsModel.Language = "klingon";

		var vm = harness.CreateViewModel();

		vm.SelectedLanguage.Code.Should().Be(LanguageCatalog.SystemLanguage);
	}

	[Fact]
	public void AvailableLanguages_AreSystemThenShippedLanguages()
	{
		var vm = new Harness().CreateViewModel();

		vm.AvailableLanguages.Select(o => o.Code)
			.Should().Equal(LanguageCatalog.SystemLanguage, "cs", "en");
		vm.AvailableLanguages.Select(o => o.DisplayName)
			.Should().Equal("LanguageSystem", "Čeština", "English");
	}

	[Fact]
	public void SelectedLanguage_Changed_AppliesImmediately()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.SelectedLanguage = vm.AvailableLanguages.First(o => o.Code == "en");

		harness.Localization.Verify(l => l.ApplyLanguage("en"), Times.Once);
	}

	[Fact]
	public async Task Save_PassesSelectedLanguage()
	{
		var harness = new Harness();
		SettingsSaveRequest? captured = null;
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.Callback((SettingsSaveRequest request, CancellationToken _) => captured = request)
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();
		vm.CloseAction = () => { };

		vm.SelectedLanguage = vm.AvailableLanguages.First(o => o.Code == "en");
		await vm.SaveCommand.ExecuteAsync(null);

		captured!.Language.Should().Be("en");
	}

	[Fact]
	public void Cancel_AfterLanguageSwitch_RestoresOriginalLanguage()
	{
		var harness = new Harness();
		harness.SettingsModel.Language = "cs";
		var vm = harness.CreateViewModel();
		vm.CloseAction = () => { };

		vm.SelectedLanguage = vm.AvailableLanguages.First(o => o.Code == "en");
		vm.CancelCommand.Execute(null);

		vm.SelectedLanguage.Code.Should().Be("cs");
		harness.Localization.Verify(l => l.ApplyLanguage("cs"), Times.Once);
	}

	[Fact]
	public void RevertPreview_WhenLanguageUnchanged_DoesNotReapply()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.RevertPreview();

		harness.Localization.Verify(l => l.ApplyLanguage(It.IsAny<string>()), Times.Never);
	}

	#endregion

	#region Theme

	[Fact]
	public void SelectedTheme_Changed_AppliesImmediately()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.SelectedTheme = "Synthwave";

		harness.Theme.Verify(t => t.ApplyThemeMode(false, "Synthwave", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
	}

	[Fact]
	public void Cancel_AfterThemeSwitch_RestoresOriginalTheme()
	{
		var harness = new Harness();
		harness.SettingsModel.Theme = "One Dark";
		var vm = harness.CreateViewModel();

		vm.SelectedTheme = "Synthwave";
		harness.Theme.Invocations.Clear();

		vm.CancelCommand.Execute(null);

		vm.SelectedTheme.Should().Be("One Dark");
		harness.Theme.Verify(t => t.ApplyThemeMode(false, "One Dark", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
	}

	[Fact]
	public void Cancel_AfterFollowSystemSwitch_RestoresWholeThemeMode()
	{
		var harness = new Harness();
		harness.SettingsModel.Theme = "One Dark";
		harness.SettingsModel.FollowSystemTheme = false;
		harness.SettingsModel.LightTheme = "Light";
		harness.SettingsModel.DarkTheme = "Midnight";
		var vm = harness.CreateViewModel();

		vm.FollowSystemTheme = true;
		vm.SelectedLightTheme = "Sandstone";
		vm.SelectedDarkTheme = "Abyss";
		harness.Theme.Invocations.Clear();

		vm.CancelCommand.Execute(null);

		vm.FollowSystemTheme.Should().BeFalse();
		vm.SelectedLightTheme.Should().Be("Light");
		vm.SelectedDarkTheme.Should().Be("Midnight");

		// One repaint on the way back, not one per restored field.
		harness.Theme.Verify(t => t.ApplyThemeMode(false, "One Dark", "Light", "Midnight"), Times.Once);
		harness.Theme.VerifyNoOtherCalls();
	}

	[Fact]
	public void RevertPreview_WhenThemeUnchanged_DoesNotReapply()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();
		harness.Theme.Invocations.Clear();

		vm.RevertPreview();

		harness.Theme.VerifyNoOtherCalls();
	}

	[Fact]
	public void RevertPreview_CalledTwice_AppliesRevertOnce()
	{
		// Cancel reverts, then the window's Closed handler reverts again on the way out.
		var harness = new Harness();
		harness.SettingsModel.Theme = "One Dark";
		var vm = harness.CreateViewModel();
		vm.SelectedTheme = "Synthwave";
		harness.Theme.Invocations.Clear();

		vm.RevertPreview();
		vm.RevertPreview();

		harness.Theme.Verify(t => t.ApplyThemeMode(false, "One Dark", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
	}

	[Fact]
	public async Task Save_AfterThemeSwitch_KeepsPreviewedTheme()
	{
		var harness = new Harness();
		harness.SettingsModel.Theme = "One Dark";
		SettingsSaveRequest? captured = null;
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.Callback((SettingsSaveRequest request, CancellationToken _) => captured = request)
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();

		vm.SelectedTheme = "Synthwave";
		harness.Theme.Invocations.Clear();

		await vm.SaveCommand.ExecuteAsync(null);

		captured!.Theme.Should().Be("Synthwave");
		// Saving keeps the preview, so there is nothing to revert.
		harness.Theme.VerifyNoOtherCalls();
	}

	#endregion

	[Fact]
	public void Constructor_LoadsCurrentSettings()
	{
		var harness = new Harness();
		harness.SettingsModel.CloseWindowBehavior = CloseWindowBehavior.ExitApplication;
		harness.SettingsModel.StartMinimized = true;
		harness.SettingsModel.CheckForUpdates = false;
		harness.SettingsModel.Theme = "One Dark";
		harness.SettingsModel.Pomodoro.WorkMinutes = 50;
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Standup"));
		harness.Autostart.SetupGet(a => a.IsEnabled).Returns(true);

		var vm = harness.CreateViewModel();

		vm.CloseWindowBehavior.Should().Be(CloseWindowBehavior.ExitApplication);
		vm.IsExitApplication.Should().BeTrue();
		vm.StartMinimized.Should().BeTrue();
		vm.CheckForUpdates.Should().BeFalse();
		vm.StartWithWindows.Should().BeTrue();
		vm.SelectedTheme.Should().Be("One Dark");
		vm.PomodoroWorkMinutes.Should().Be(50);
		vm.FavoriteWorkItems.Should().ContainSingle(f => f.Name == "Standup");
	}

	[Fact]
	public async Task Save_PassesCurrentValuesToOrchestratorAndCloses()
	{
		var harness = new Harness();
		SettingsSaveRequest? captured = null;
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.Callback((SettingsSaveRequest request, CancellationToken _) => captured = request)
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;

		vm.IsExitApplication = true;
		vm.StartMinimized = true;
		vm.PomodoroEnabled = true;
		vm.PomodoroWorkMinutes = 45;

		await vm.SaveCommand.ExecuteAsync(null);

		captured.Should().NotBeNull();
		captured!.CloseWindowBehavior.Should().Be(CloseWindowBehavior.ExitApplication);
		captured.StartMinimized.Should().BeTrue();
		captured.Pomodoro.Enabled.Should().BeTrue();
		captured.Pomodoro.WorkMinutes.Should().Be(45);
		vm.DialogResult.Should().BeTrue();
		closed.Should().BeTrue();
	}

	[Fact]
	public async Task Save_OrchestratorFails_KeepsDialogOpen()
	{
		var harness = new Harness();
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("disk full"));
		var vm = harness.CreateViewModel();
		var closed = false;
		vm.CloseAction = () => closed = true;

		await vm.SaveCommand.ExecuteAsync(null);

		vm.DialogResult.Should().BeFalse();
		closed.Should().BeFalse();
	}

	[Fact]
	public void AddAndSaveFavorite_AppendsItemAndSelectsIt()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.AddFavoriteCommand.Execute(null);
		vm.IsAddingFavorite.Should().BeTrue();

		vm.EditingFavoriteName = "Daily standup";
		vm.EditingFavoriteTicket = "PROJ-1";
		vm.SaveFavoriteCommand.Execute(null);

		vm.FavoriteWorkItems.Should().ContainSingle();
		vm.SelectedFavorite!.Name.Should().Be("Daily standup");
		vm.SelectedFavorite.TicketId.Should().Be("PROJ-1");
		vm.IsAddingFavorite.Should().BeFalse();
	}

	[Fact]
	public void SaveFavorite_EditingExisting_UpdatesInPlace()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Old name"));
		var vm = harness.CreateViewModel();
		vm.SelectedFavorite = vm.FavoriteWorkItems[0];

		vm.EditingFavoriteName = "New name";
		vm.SaveFavoriteCommand.Execute(null);

		vm.FavoriteWorkItems.Should().ContainSingle();
		vm.FavoriteWorkItems[0].Name.Should().Be("New name");
	}

	[Fact]
	public void SaveFavoriteCommand_RequiresName()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();
		vm.AddFavoriteCommand.Execute(null);

		vm.SaveFavoriteCommand.CanExecute(null).Should().BeFalse();

		vm.EditingFavoriteName = "Named";
		vm.SaveFavoriteCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public void RemoveFavorite_SelectsNeighbour()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("First"));
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Second"));
		var vm = harness.CreateViewModel();
		vm.SelectedFavorite = vm.FavoriteWorkItems[0];

		vm.RemoveFavoriteCommand.Execute(null);

		vm.FavoriteWorkItems.Should().ContainSingle(f => f.Name == "Second");
		vm.SelectedFavorite!.Name.Should().Be("Second");
	}

	[Fact]
	public void MoveFavorite_RespectsBoundariesAndReorders()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("First"));
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Second"));
		var vm = harness.CreateViewModel();

		vm.SelectedFavorite = vm.FavoriteWorkItems[0];
		vm.MoveFavoriteUpCommand.CanExecute(null).Should().BeFalse("the item is already first");
		vm.MoveFavoriteDownCommand.CanExecute(null).Should().BeTrue();

		vm.MoveFavoriteDownCommand.Execute(null);

		vm.FavoriteWorkItems.Select(f => f.Name).Should().ContainInOrder("Second", "First");
		vm.MoveFavoriteDownCommand.CanExecute(null).Should().BeFalse("the item is now last");
		vm.MoveFavoriteUpCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public void TestConnectionCommand_WithoutPlugins_CannotExecute()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.HasPlugins.Should().BeFalse();
		vm.TestConnectionCommand.CanExecute(null).Should().BeFalse();
	}

	[Fact]
	public void EditingFavorite_DoesNotTouchStoredSettingsUntilSaved()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Old name"));
		var vm = harness.CreateViewModel();
		vm.SelectedFavorite = vm.FavoriteWorkItems[0];

		vm.EditingFavoriteName = "New name";
		vm.SaveFavoriteCommand.Execute(null);

		vm.FavoriteWorkItems[0].Name.Should().Be("New name");
		harness.SettingsModel.FavoriteWorkItems[0].Name.Should()
			.Be("Old name", "the dialog edits a copy, so Cancel leaves the stored favorite alone");
	}

	[Fact]
	public void LoadedFavorites_KeepTheirIdSoSavingUpdatesRatherThanReplaces()
	{
		var harness = new Harness();
		var stored = Favorite("Standup");
		harness.SettingsModel.FavoriteWorkItems.Add(stored);

		var vm = harness.CreateViewModel();

		vm.FavoriteWorkItems[0].Should().NotBeSameAs(stored);
		vm.FavoriteWorkItems[0].Id.Should().Be(stored.Id);
	}
}
