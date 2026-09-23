// Copyright (c) 2023-2026 ktsu-dev contributors

[assembly: DoNotParallelize]

namespace ktsu.IntervalAction.Test;

using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class IntervalActionTests
{
	[TestMethod]
	public async Task ActionExecutesAfterIntervalFromLastCompletion()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(100),
			ActionInterval = TimeSpan.FromMilliseconds(300),
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastCompletion
		};

		IntervalAction actionInstance = IntervalAction.Start(options);

		// Wait long enough to allow multiple executions.
		await Task.Delay(1100).ConfigureAwait(false);
		actionInstance.Stop();

		// Expect at least 3 executions.
		Assert.IsGreaterThanOrEqualTo(3, counter, $"Expected at least 3 executions, but got {counter}.");

		actionInstance.RethrowExceptions();
	}

	[TestMethod]
	public async Task NoOverlappingExecutions()
	{
		int executions = 0;
		// Simulate a long-running action so that overlapping is prevented.
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(50),
			ActionInterval = TimeSpan.FromMilliseconds(100),
			Action = () =>
			{
				Interlocked.Increment(ref executions);
				// Simulate a long running task.
				Thread.Sleep(500);
			},
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction actionInstance = IntervalAction.Start(options);

		// Allow several polling cycles.
		await Task.Delay(1200).ConfigureAwait(false);
		actionInstance.Stop();

		// With a long-running action, overlapping should be prevented resulting in fewer executions.
		Assert.IsLessThanOrEqualTo(3, executions, $"Expected no overlapping executions, but got {executions} executions.");

		actionInstance.RethrowExceptions();
	}

	[TestMethod]
	public async Task RestartResumesExecution()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(100),
			ActionInterval = TimeSpan.FromMilliseconds(300),
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastCompletion
		};

		IntervalAction actionInstance = IntervalAction.Start(options);

		// Allow some executions.
		await Task.Delay(700).ConfigureAwait(false);
		actionInstance.Stop();
		int countAfterStop = counter;

		// Wait to ensure no further execution occurs after stopping.
		await Task.Delay(500).ConfigureAwait(false);
		Assert.AreEqual(countAfterStop, counter, "No executions should occur after stopping.");

		// Restart the polling.
		await actionInstance.RestartAsync().ConfigureAwait(false);
		await Task.Delay(700).ConfigureAwait(false);
		actionInstance.Stop();
		Assert.IsGreaterThan(countAfterStop, counter, "Executions should resume after restarting.");

		actionInstance.RethrowExceptions();
	}

	[TestMethod]
	public void StartNullOptionsThrows()
	{
		Assert.ThrowsExactly<ArgumentNullException>(() => IntervalAction.Start(null!));
	}

	[TestMethod]
	public void StartNullActionThrows()
	{
		// Arrange
		// Using null-forgiving operator to bypass the compile-time requirement.
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromMilliseconds(10),
			Action = null!,
			IntervalType = IntervalType.FromLastCompletion
		};
		Assert.ThrowsExactly<ArgumentNullException>(() => IntervalAction.Start(options));
	}

	[TestMethod]
	public async Task RestartStopsPreviousPollingTaskAndStartsNewOne()
	{
		// Arrange
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromMilliseconds(10),
			Action = () => counter++,
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		// Allow polling to run for a moment.
		await Task.Delay(50).ConfigureAwait(false);

		// Act: Restart polling
		Task oldPollingTask = intervalAction.PollingTask;
		await intervalAction.RestartAsync().ConfigureAwait(false);

		// Assert: The old polling task should have completed.
		Assert.IsTrue(oldPollingTask.IsCompleted, "Old polling task should have completed after restart");

		// Allow new polling task to run a bit.
		await Task.Delay(30).ConfigureAwait(false);
		Assert.IsGreaterThan(0, counter, "Counter should have incremented after restart");

		intervalAction.Stop();

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task StopPollingTaskStopsExecuting()
	{
		// Arrange
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () => counter++,
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		// Allow the polling loop to execute a few times.
		await Task.Delay(30).ConfigureAwait(false);

		// Act
		intervalAction.Stop();
		// Await the polling task to ensure the loop has exited.
		await intervalAction.PollingTask.ConfigureAwait(false);
		int counterAfterStop = counter;

		// Wait additional time to verify no further actions are executed.
		await Task.Delay(30).ConfigureAwait(false);
		Assert.AreEqual(counterAfterStop, counter);

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task RethrowExceptionsThrowsException()
	{
		// Arrange
		string exceptionMessage = "Test exception message";
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () => throw new InvalidOperationException(exceptionMessage),
			IntervalType = IntervalType.FromLastStart
		};
		IntervalAction intervalAction = IntervalAction.Start(options);
		// Allow the polling loop to execute a few times.
		await Task.Delay(30).ConfigureAwait(false);
		// Act
		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);
		Assert.AreEqual(exceptionMessage, exception.Message);
		intervalAction.Stop();
	}

	[TestMethod]
	public async Task RestartResumesPollingAfterActionThrows()
	{
		// Arrange: the action throws on its first invocation only, so if polling really resumes
		// the counter keeps climbing afterwards.
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () =>
			{
				if (Interlocked.Increment(ref counter) == 1)
				{
					throw new InvalidOperationException("Test exception message");
				}
			},
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);

		// Let the throw happen and the polling loop observe it.
		await Task.Delay(200).ConfigureAwait(false);
		_ = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);

		int countAtFault = Volatile.Read(ref counter);

		// Act: the documented recovery — observe the failure, then restart.
		await intervalAction.RestartAsync().ConfigureAwait(false);
		await Task.Delay(200).ConfigureAwait(false);
		intervalAction.Stop();

		// Assert
		Assert.IsGreaterThan(countAtFault, Volatile.Read(ref counter), "Polling should resume after a restart following an action exception.");

		// The restart replaced the faulted polling task, so nothing stale is left to rethrow.
		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task RestartDoesNotRethrowTheStaleActionException()
	{
		// Arrange
		string exceptionMessage = "Test exception message";
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () => throw new InvalidOperationException(exceptionMessage),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(200).ConfigureAwait(false);
		_ = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);

		// Act & Assert: restarting must not surface the exception that already killed the old loop,
		// even though this action goes on throwing — the restart itself has to complete.
		await intervalAction.RestartAsync().ConfigureAwait(false);

		intervalAction.Stop();
	}

	[TestMethod]
	public async Task ConcurrentRestartsLeaveTheInstancePolling()
	{
		// Arrange
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);

		// Act: restart from several threads at once. Restarts are chained rather than interleaved,
		// so every one of these completes and the last one leaves a running loop behind.
		await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(intervalAction.RestartAsync))).ConfigureAwait(false);

		int countAfterRestarts = Volatile.Read(ref counter);
		await Task.Delay(200).ConfigureAwait(false);
		intervalAction.Stop();
		await intervalAction.PollingTask.ConfigureAwait(false);

		// Assert: the instance is still polling, and Stop still ends the loop it names.
		Assert.IsGreaterThan(countAfterRestarts, Volatile.Read(ref counter), "The instance should still be polling after concurrent restarts.");
		Assert.IsTrue(intervalAction.PollingTask.IsCompleted, "Stop should end the polling task the instance names.");

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task ZeroIntervalExecutesQuickly()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(40).ConfigureAwait(false);
		intervalAction.Stop();
		Assert.IsGreaterThan(0, counter, $"Expected at least one execution, got {counter}.");

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task NegativeIntervalNeverExecutes()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromMilliseconds(-1),
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(50).ConfigureAwait(false);
		intervalAction.Stop();
		Assert.AreEqual(0, counter, $"Expected zero executions for negative interval, got {counter}.");

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task StopIsIdempotent()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromMilliseconds(10),
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(40).ConfigureAwait(false);
		intervalAction.Stop();
		await intervalAction.PollingTask.ConfigureAwait(false);
		int countAfterFirstStop = counter;

		// Call Stop again; should not throw and should not resume execution
		intervalAction.Stop();
		await Task.Delay(30).ConfigureAwait(false);
		Assert.AreEqual(countAfterFirstStop, counter, "Stop should be idempotent and prevent further executions.");

		intervalAction.RethrowExceptions();
	}
}
