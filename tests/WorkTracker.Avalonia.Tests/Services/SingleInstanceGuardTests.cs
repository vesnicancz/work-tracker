using FluentAssertions;
using WorkTracker.Avalonia.Services;

namespace WorkTracker.Avalonia.Tests.Services;

public sealed class SingleInstanceGuardTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), $"wt-instance-{Guid.NewGuid():N}");

	public void Dispose()
	{
		Directory.Delete(_directory, recursive: true);
	}

	[Fact]
	public void TryAcquire_WhileHeld_ReturnsNull()
	{
		using var first = SingleInstanceGuard.TryAcquire(_directory);

		first.Should().NotBeNull();
		SingleInstanceGuard.TryAcquire(_directory).Should().BeNull();
	}

	[Fact]
	public void TryAcquire_AfterRelease_Succeeds()
	{
		SingleInstanceGuard.TryAcquire(_directory)!.Dispose();

		using var second = SingleInstanceGuard.TryAcquire(_directory);

		second.Should().NotBeNull();
	}

	[Fact]
	public void TryAcquire_InAnotherDirectory_IsIndependent()
	{
		var other = _directory + "-other";
		try
		{
			using var first = SingleInstanceGuard.TryAcquire(_directory);
			using var second = SingleInstanceGuard.TryAcquire(other);

			second.Should().NotBeNull();
		}
		finally
		{
			Directory.Delete(other, recursive: true);
		}
	}

	[Fact]
	public async Task SignalRunningInstance_ReachesTheListeningInstance()
	{
		using var guard = SingleInstanceGuard.TryAcquire(_directory)!;
		var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		guard.Listen(() => activated.TrySetResult());

		SingleInstanceGuard.SignalRunningInstance(_directory, TimeSpan.FromSeconds(5)).Should().BeTrue();

		await activated.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
	}

	[Fact]
	public void SignalRunningInstance_WithNobodyListening_ReturnsFalse()
	{
		Directory.CreateDirectory(_directory);

		SingleInstanceGuard.SignalRunningInstance(_directory, TimeSpan.FromMilliseconds(100)).Should().BeFalse();
	}
}
