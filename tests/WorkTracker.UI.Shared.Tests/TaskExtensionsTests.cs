using FluentAssertions;

namespace WorkTracker.UI.Shared.Tests;

public class TaskExtensionsTests
{
	[Fact]
	public async Task SafeFireAndForgetAsync_SuccessfulTask_DoesNotCallHandler()
	{
		var handlerCalled = false;

		await Task.CompletedTask.SafeFireAndForgetAsync(_ => handlerCalled = true);

		handlerCalled.Should().BeFalse();
	}

	[Fact]
	public async Task SafeFireAndForgetAsync_FaultedTask_CallsHandler()
	{
		Exception? caught = null;
		var expected = new InvalidOperationException("test error");

		await Task.FromException(expected).SafeFireAndForgetAsync(ex => caught = ex);

		caught.Should().BeSameAs(expected);
	}

	[Fact]
	public async Task SafeFireAndForgetAsync_OperationCanceled_DoesNotCallHandler()
	{
		var handlerCalled = false;

		await Task.FromCanceled(new CancellationToken(true))
			.SafeFireAndForgetAsync(_ => handlerCalled = true);

		handlerCalled.Should().BeFalse();
	}

	[Fact]
	public async Task SafeFireAndForgetAsync_TaskCanceledException_DoesNotCallHandler()
	{
		var handlerCalled = false;
		var task = Task.FromException(new TaskCanceledException("cancelled"));

		await task.SafeFireAndForgetAsync(_ => handlerCalled = true);

		handlerCalled.Should().BeFalse();
	}

	[Fact]
	public async Task SafeFireAndForgetAsync_NullTask_ThrowsArgumentNullException()
	{
		Task task = null!;

		var act = () => task.SafeFireAndForgetAsync(_ => { });

		await act.Should().ThrowAsync<ArgumentNullException>();
	}

	[Fact]
	public async Task SafeFireAndForgetAsync_DelayedFault_CallsHandler()
	{
		Exception? caught = null;

		await DelayedFault().SafeFireAndForgetAsync(ex => caught = ex);

		caught.Should().BeOfType<InvalidOperationException>();

		static async Task DelayedFault()
		{
			await Task.Yield();
			throw new InvalidOperationException("delayed");
		}
	}
}
