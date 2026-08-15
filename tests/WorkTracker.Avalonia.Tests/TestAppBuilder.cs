using Avalonia;
using Avalonia.Headless;
using WorkTracker.Avalonia;

namespace WorkTracker.Avalonia.Tests;

/// <summary>
/// Builds the real <see cref="App"/> (including App.axaml resources and styles) on the
/// headless platform, so UI tests run against the production application class.
/// Avalonia.Headless.XUnit ([AvaloniaFact]) is compiled against xunit.v3 3.x and crashes
/// test discovery under xunit.v3 4.x, so the headless session is managed manually here.
/// </summary>
public static class TestAppBuilder
{
	public static AppBuilder BuildAvaloniaApp() =>
		AppBuilder.Configure<App>()
			.UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// One headless Avalonia session (UI thread + dispatcher loop) shared by the whole test
/// assembly. Intentionally never disposed: HeadlessUnitTestSession.Dispose deadlocks when
/// called from a test-framework cleanup thread, and Avalonia's own XUnit integration also
/// keeps sessions alive for the process lifetime (the loop runs on a background thread).
/// </summary>
public static class UiThread
{
	private static readonly Lazy<HeadlessUnitTestSession> s_session = new(
		() => HeadlessUnitTestSession.StartNew(typeof(TestAppBuilder)),
		LazyThreadSafetyMode.ExecutionAndPublication);

	public static Task Dispatch(Action body) =>
		s_session.Value.Dispatch(body, CancellationToken.None);

	public static Task Dispatch(Func<Task> body) =>
		s_session.Value.Dispatch(async () =>
		{
			await body();
			return true;
		}, CancellationToken.None);
}
