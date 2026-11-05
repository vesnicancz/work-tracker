using FluentAssertions;
using Moq;
using WorkTracker.Application.Plugins;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests;

public class AppShutdownTests
{
	private readonly List<string> _steps = [];
	private readonly Mock<IPomodoroService> _pomodoro = new();
	private readonly Mock<IPluginManager> _pluginManager = new();

	public AppShutdownTests()
	{
		_pomodoro.Setup(p => p.DisposeAsync()).Callback(() => _steps.Add("pomodoro")).Returns(ValueTask.CompletedTask);
		_pluginManager.Setup(p => p.DisposeAsync()).Callback(() => _steps.Add("plugins")).Returns(ValueTask.CompletedTask);
	}

	[Fact]
	public async Task StopPomodoroAndPlugins_StopsThePomodoroWhileTheIndicatorsAreStillRunning()
	{
		await App.StopPomodoroAndPluginsAsync(Task.CompletedTask, _pomodoro.Object, _pluginManager.Object);

		_steps.Should().Equal("pomodoro", "plugins");
	}

	[Fact]
	public async Task StopPomodoroAndPlugins_WaitsForPluginsStillStarting()
	{
		var pluginStartup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var stopping = App.StopPomodoroAndPluginsAsync(pluginStartup.Task, _pomodoro.Object, _pluginManager.Object);

		_steps.Should().BeEmpty();

		pluginStartup.SetResult();
		await stopping;

		_steps.Should().Equal("pomodoro", "plugins");
	}
}
