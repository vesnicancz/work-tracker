using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using WorkTracker.Avalonia.ViewModels;
using WorkTracker.Plugin.Abstractions;
using WorkTracker.Avalonia.Models;
using WorkTracker.Avalonia.Orchestrators;
using WorkTracker.Avalonia.Services;
using WorkTracker.Tests.Common.Helpers;

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
			Orchestrator.Setup(o => o.ValidatePluginConfigurationsAsync(It.IsAny<IEnumerable<PluginSettingsEntry>>(), It.IsAny<CancellationToken>()))
				.ReturnsAsync([]);
			Localization.Setup(l => l[It.IsAny<string>()]).Returns((string key) => key);
			Localization.Setup(l => l.GetFormattedString(It.IsAny<string>(), It.IsAny<object[]>()))
				.Returns((string key, object[] args) => $"{key}: {string.Join(", ", args)}");
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

	private static PluginSettingsEntry Plugin<TPlugin>(bool isEnabled)
		where TPlugin : class, IPlugin
	{
		var plugin = new Mock<TPlugin>();
		plugin.Setup(p => p.GetConfigurationFields()).Returns(new List<PluginConfigurationField>());

		return new PluginSettingsEntry(plugin.Object, isEnabled, new Dictionary<string, string>());
	}

	#region Notification settings

	[Fact]
	public void HasEnabledRemoteControlPlugin_WithoutPlugins_IsFalse()
	{
		var vm = new Harness().CreateViewModel();

		vm.Plugins.HasEnabledRemoteControlPlugin.Should().BeFalse();
	}

	[Fact]
	public void HasEnabledRemoteControlPlugin_WithOnlyAnotherEnabledPlugin_IsFalse()
	{
		var harness = new Harness();
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns([Plugin<IStatusIndicatorPlugin>(isEnabled: true)]);

		var vm = harness.CreateViewModel();

		vm.Plugins.HasEnabledRemoteControlPlugin.Should().BeFalse();
	}

	[Fact]
	public void HasEnabledRemoteControlPlugin_WithEnabledRemoteControlPlugin_IsTrue()
	{
		var harness = new Harness();
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns([Plugin<IRemoteControlPlugin>(isEnabled: true)]);

		var vm = harness.CreateViewModel();

		vm.Plugins.HasEnabledRemoteControlPlugin.Should().BeTrue();
	}

	[Fact]
	public void HasEnabledRemoteControlPlugin_FollowsThePluginBeingSwitched()
	{
		var harness = new Harness();
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns([Plugin<IRemoteControlPlugin>(isEnabled: false)]);
		var vm = harness.CreateViewModel();
		using var monitor = vm.Plugins.Monitor();

		vm.Plugins.Items[0].IsEnabled = true;

		vm.Plugins.HasEnabledRemoteControlPlugin.Should().BeTrue();
		monitor.Should().RaisePropertyChangeFor(x => x.HasEnabledRemoteControlPlugin);
	}

	[Fact]
	public async Task Save_WithFeatureTurnedOff_KeepsItsNotificationSettings()
	{
		var harness = new Harness();
		harness.SettingsModel.ShowRemoteControlNotifications = false;
		harness.SettingsModel.Pomodoro.Enabled = false;
		harness.SettingsModel.Pomodoro.ShowSystemNotifications = false;
		SettingsSaveRequest? saved = null;
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.Callback((SettingsSaveRequest request, CancellationToken _) => saved = request)
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();

		await vm.SaveCommand.ExecuteAsync(null);

		saved!.ShowRemoteControlNotifications.Should().BeFalse();
		saved.Pomodoro.ShowSystemNotifications.Should().BeFalse();
	}

	#endregion Notification settings

	#region Language

	[Fact]
	public void Constructor_SeedsSelectedLanguageFromSettings()
	{
		var harness = new Harness();
		harness.SettingsModel.Language = "cs";

		var vm = harness.CreateViewModel();

		vm.Appearance.SelectedLanguage.Code.Should().Be("cs");
	}

	[Fact]
	public void Constructor_UnknownStoredLanguage_FallsBackToSystem()
	{
		var harness = new Harness();
		harness.SettingsModel.Language = "klingon";

		var vm = harness.CreateViewModel();

		vm.Appearance.SelectedLanguage.Code.Should().Be(LanguageCatalog.SystemLanguage);
	}

	[Fact]
	public void AvailableLanguages_AreSystemThenShippedLanguages()
	{
		var vm = new Harness().CreateViewModel();

		vm.Appearance.AvailableLanguages.Select(o => o.Code)
			.Should().Equal(LanguageCatalog.SystemLanguage, "cs", "en");
		vm.Appearance.AvailableLanguages.Select(o => o.DisplayName)
			.Should().Equal("LanguageSystem", "Čeština", "English");
	}

	[Fact]
	public void SelectedLanguage_Changed_AppliesImmediately()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.Appearance.SelectedLanguage = vm.Appearance.AvailableLanguages.First(o => o.Code == "en");

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

		vm.Appearance.SelectedLanguage = vm.Appearance.AvailableLanguages.First(o => o.Code == "en");
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

		vm.Appearance.SelectedLanguage = vm.Appearance.AvailableLanguages.First(o => o.Code == "en");
		vm.CancelCommand.Execute(null);

		vm.Appearance.SelectedLanguage.Code.Should().Be("cs");
		harness.Localization.Verify(l => l.ApplyLanguage("cs"), Times.Once);
	}

	[Fact]
	public void SelectedLanguage_Changed_RefreshesTheVersionLine()
	{
		var vm = new Harness().CreateViewModel();
		using var monitor = vm.Monitor();

		vm.Appearance.SelectedLanguage = vm.Appearance.AvailableLanguages.First(o => o.Code == "en");

		monitor.Should().RaisePropertyChangeFor(x => x.AppVersionDisplay);
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

		vm.Appearance.SelectedTheme = "Synthwave";

		harness.Theme.Verify(t => t.ApplyThemeMode(false, "Synthwave", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
	}

	[Fact]
	public void Cancel_AfterThemeSwitch_RestoresOriginalTheme()
	{
		var harness = new Harness();
		harness.SettingsModel.Theme = "One Dark";
		var vm = harness.CreateViewModel();

		vm.Appearance.SelectedTheme = "Synthwave";
		harness.Theme.Invocations.Clear();

		vm.CancelCommand.Execute(null);

		vm.Appearance.SelectedTheme.Should().Be("One Dark");
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

		vm.Appearance.FollowSystemTheme = true;
		vm.Appearance.SelectedLightTheme = "Sandstone";
		vm.Appearance.SelectedDarkTheme = "Abyss";
		harness.Theme.Invocations.Clear();

		vm.CancelCommand.Execute(null);

		vm.Appearance.FollowSystemTheme.Should().BeFalse();
		vm.Appearance.SelectedLightTheme.Should().Be("Light");
		vm.Appearance.SelectedDarkTheme.Should().Be("Midnight");

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
		vm.Appearance.SelectedTheme = "Synthwave";
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

		vm.Appearance.SelectedTheme = "Synthwave";
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
		harness.SettingsModel.ShowRemoteControlNotifications = false;
		harness.SettingsModel.Pomodoro.ShowSystemNotifications = false;
		harness.SettingsModel.Theme = "One Dark";
		harness.SettingsModel.Pomodoro.WorkMinutes = 50;
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Standup"));
		harness.Autostart.SetupGet(a => a.IsEnabled).Returns(true);

		var vm = harness.CreateViewModel();

		vm.CloseWindowBehavior.Should().Be(CloseWindowBehavior.ExitApplication);
		vm.IsExitApplication.Should().BeTrue();
		vm.StartMinimized.Should().BeTrue();
		vm.CheckForUpdates.Should().BeFalse();
		vm.ShowRemoteControlNotifications.Should().BeFalse();
		vm.Pomodoro.ShowSystemNotifications.Should().BeFalse();
		vm.StartWithWindows.Should().BeTrue();
		vm.Appearance.SelectedTheme.Should().Be("One Dark");
		vm.Pomodoro.WorkMinutes.Should().Be(50);
		vm.Favorites.Items.Should().ContainSingle(f => f.Name == "Standup");
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
		vm.Pomodoro.Enabled = true;
		vm.Pomodoro.WorkMinutes = 45;

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

		vm.Favorites.AddCommand.Execute(null);
		vm.Favorites.IsAdding.Should().BeTrue();

		vm.Favorites.EditingName = "Daily standup";
		vm.Favorites.EditingTicket = "PROJ-1";
		vm.Favorites.SaveCommand.Execute(null);

		vm.Favorites.Items.Should().ContainSingle();
		vm.Favorites.SelectedItem!.Name.Should().Be("Daily standup");
		vm.Favorites.SelectedItem.TicketId.Should().Be("PROJ-1");
		vm.Favorites.IsAdding.Should().BeFalse();
	}

	[Fact]
	public void SaveFavorite_EditingExisting_UpdatesInPlace()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Old name"));
		var vm = harness.CreateViewModel();
		vm.Favorites.SelectedItem = vm.Favorites.Items[0];

		vm.Favorites.EditingName = "New name";
		vm.Favorites.SaveCommand.Execute(null);

		vm.Favorites.Items.Should().ContainSingle();
		vm.Favorites.Items[0].Name.Should().Be("New name");
	}

	[Fact]
	public void SaveFavoriteCommand_RequiresName()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();
		vm.Favorites.AddCommand.Execute(null);

		vm.Favorites.SaveCommand.CanExecute(null).Should().BeFalse();

		vm.Favorites.EditingName = "Named";
		vm.Favorites.SaveCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public void RemoveFavorite_SelectsNeighbour()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("First"));
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Second"));
		var vm = harness.CreateViewModel();
		vm.Favorites.SelectedItem = vm.Favorites.Items[0];

		vm.Favorites.RemoveCommand.Execute(null);

		vm.Favorites.Items.Should().ContainSingle(f => f.Name == "Second");
		vm.Favorites.SelectedItem!.Name.Should().Be("Second");
	}

	[Fact]
	public void MoveFavorite_RespectsBoundariesAndReorders()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("First"));
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Second"));
		var vm = harness.CreateViewModel();

		vm.Favorites.SelectedItem = vm.Favorites.Items[0];
		vm.Favorites.MoveUpCommand.CanExecute(null).Should().BeFalse("the item is already first");
		vm.Favorites.MoveDownCommand.CanExecute(null).Should().BeTrue();

		vm.Favorites.MoveDownCommand.Execute(null);

		vm.Favorites.Items.Select(f => f.Name).Should().ContainInOrder("Second", "First");
		vm.Favorites.MoveDownCommand.CanExecute(null).Should().BeFalse("the item is now last");
		vm.Favorites.MoveUpCommand.CanExecute(null).Should().BeTrue();
	}

	[Fact]
	public void TestConnectionCommand_WithoutPlugins_CannotExecute()
	{
		var harness = new Harness();
		var vm = harness.CreateViewModel();

		vm.Plugins.HasPlugins.Should().BeFalse();
		vm.Plugins.TestConnectionCommand.CanExecute(null).Should().BeFalse();
	}

	[Fact]
	public void EditingFavorite_DoesNotTouchStoredSettingsUntilSaved()
	{
		var harness = new Harness();
		harness.SettingsModel.FavoriteWorkItems.Add(Favorite("Old name"));
		var vm = harness.CreateViewModel();
		vm.Favorites.SelectedItem = vm.Favorites.Items[0];

		vm.Favorites.EditingName = "New name";
		vm.Favorites.SaveCommand.Execute(null);

		vm.Favorites.Items[0].Name.Should().Be("New name");
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

		vm.Favorites.Items[0].Should().NotBeSameAs(stored);
		vm.Favorites.Items[0].Id.Should().Be(stored.Id);
	}

	#region Plugins

	[Fact]
	public void LoadedPlugin_HasStoredValuesOverItsDefaultsAndKeysWithoutAField()
	{
		var descriptor = MockPluginFactory.CreatePlugin("tempo", "Tempo",
			new PluginConfigurationField { Key = "Url", Label = "Url", DefaultValue = "https://default" },
			new PluginConfigurationField { Key = "Timeout", Label = "Timeout", DefaultValue = "30" });
		var harness = new Harness();
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns(
		[
			new PluginSettingsEntry(descriptor.Object, true, new Dictionary<string, string> { ["Url"] = "https://stored", ["Hidden"] = "kept" })
		]);

		var plugin = harness.CreateViewModel().Plugins.Items.Single();

		plugin.IsEnabled.Should().BeTrue();
		plugin.ConfigurationFields.Single(f => f.Key == "Url").Value.Should().Be("https://stored");
		plugin.Configuration.Should().Contain("Timeout", "30").And.Contain("Hidden", "kept");
	}

	[Fact]
	public async Task Save_SendsWhatTheUserLeftOnThePlugin()
	{
		var harness = new Harness();
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns([Plugin<IStatusIndicatorPlugin>(isEnabled: false)]);
		SettingsSaveRequest? saved = null;
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.Callback((SettingsSaveRequest request, CancellationToken _) => saved = request)
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();

		vm.Plugins.Items[0].IsEnabled = true;
		vm.Plugins.Items[0].Configuration["Color"] = "Red";
		await vm.SaveCommand.ExecuteAsync(null);

		var entry = saved!.Plugins.Should().ContainSingle().Subject;
		entry.IsEnabled.Should().BeTrue();
		entry.Configuration.Should().Contain("Color", "Red");
	}

	[Fact]
	public async Task TestConnection_TestsWhatIsInTheFormAndShowsTheResult()
	{
		var harness = new Harness();
		var descriptor = MockPluginFactory.CreatePlugin("luxafor", "Luxafor");
		harness.Orchestrator.Setup(o => o.LoadPlugins()).Returns([new PluginSettingsEntry(descriptor.Object, false, new Dictionary<string, string>())]);
		PluginSettingsEntry? tested = null;
		harness.Orchestrator
			.Setup(o => o.TestConnectionAsync(It.IsAny<PluginSettingsEntry>(), It.IsAny<IProgress<string>?>(), It.IsAny<CancellationToken>()))
			.Callback((PluginSettingsEntry entry, IProgress<string>? _, CancellationToken _) => tested = entry)
			.ReturnsAsync("✓ Connection successful");
		var vm = harness.CreateViewModel();

		vm.Plugins.Items[0].Configuration["Color"] = "Unsaved";
		await vm.Plugins.TestConnectionCommand.ExecuteAsync(null);

		tested!.Configuration.Should().Contain("Color", "Unsaved");
		vm.Plugins.TestConnectionResult.Should().Be("✓ Connection successful");
	}

	#endregion

	#region Saving

	[Fact]
	public async Task Save_WhenItFails_ReportsWhyAndLeavesTheDialogOpen()
	{
		var harness = new Harness();
		harness.Orchestrator
			.Setup(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("No credential store has been selected."));
		var closed = false;
		var vm = harness.CreateViewModel();
		vm.CloseAction = () => closed = true;

		await vm.SaveCommand.ExecuteAsync(null);

		closed.Should().BeFalse("a failed save has nothing to close the dialog over");
		vm.HasSaveError.Should().BeTrue();
		vm.SaveError.Should().Contain("No credential store has been selected.");
	}

	[Fact]
	public async Task Save_WithInvalidPluginConfiguration_ShowsTheErrorsAndDoesNotSave()
	{
		var harness = new Harness();
		harness.Orchestrator
			.Setup(o => o.ValidatePluginConfigurationsAsync(It.IsAny<IEnumerable<PluginSettingsEntry>>(), It.IsAny<CancellationToken>()))
			.ReturnsAsync(["Luxafor: Bad colour"]);
		var closed = false;
		var vm = harness.CreateViewModel();
		vm.CloseAction = () => closed = true;

		await vm.SaveCommand.ExecuteAsync(null);

		closed.Should().BeFalse();
		vm.SaveError.Should().Contain("PluginConfigurationInvalid").And.Contain("Luxafor: Bad colour");
		harness.Orchestrator.Verify(
			o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()), Times.Never);
	}

	[Fact]
	public async Task Save_AfterAFailedAttempt_ClearsTheMessageWhenItSucceeds()
	{
		var harness = new Harness();
		harness.Orchestrator
			.SetupSequence(o => o.SaveSettingsAsync(It.IsAny<SettingsSaveRequest>(), It.IsAny<CancellationToken>()))
			.ThrowsAsync(new InvalidOperationException("keyring unavailable"))
			.Returns(Task.CompletedTask);
		var vm = harness.CreateViewModel();
		vm.CloseAction = () => { };

		await vm.SaveCommand.ExecuteAsync(null);
		await vm.SaveCommand.ExecuteAsync(null);

		vm.HasSaveError.Should().BeFalse();
		vm.SaveError.Should().BeNull();
	}

	#endregion
}
