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
	[DataRow(-2000)]
	[DataRow(0)]
	[DataRow(-1)] // Timeout.InfiniteTimeSpan
	public void StartNonPositivePollingIntervalThrows(int pollingIntervalMilliseconds)
	{
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds),
			ActionInterval = TimeSpan.FromMilliseconds(10),
			Action = () => { },
			IntervalType = IntervalType.FromLastCompletion
		};
		Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => IntervalAction.Start(options));
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
	public async Task RestartRightAfterStopLeavesOnlyOnePollingLoop()
	{
		// Arrange
		int counter = 0;
		TimeSpan pollingInterval = TimeSpan.FromMilliseconds(200);
		IntervalActionOptions options = new()
		{
			PollingInterval = pollingInterval,
			ActionInterval = TimeSpan.Zero,
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		// Let the loop run its first tick and enter its delay.
		await Task.Delay(50).ConfigureAwait(false);

		// Act: stop, then restart while the old loop is still inside its delay
		Task oldPollingTask = intervalAction.PollingTask;
		intervalAction.Stop();
		await intervalAction.RestartAsync().ConfigureAwait(false);

		// Assert: the old loop has ended rather than being left to resume alongside the new one
		Assert.IsTrue(oldPollingTask.IsCompleted, "Old polling task should have completed before the restart started a new one");
		Assert.AreNotSame(oldPollingTask, intervalAction.PollingTask);

		int counterAtRestart = Volatile.Read(ref counter);
		System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
		await Task.Delay(TimeSpan.FromTicks(pollingInterval.Ticks * 10)).ConfigureAwait(false);
		intervalAction.Stop();
		await intervalAction.PollingTask.ConfigureAwait(false);
		stopwatch.Stop();

		// One loop starts the action at most once per polling interval, plus its first tick
		int executions = Volatile.Read(ref counter) - counterAtRestart;
		int maxExecutionsForOneLoop = (int)(stopwatch.Elapsed.Ticks / pollingInterval.Ticks) + 1;
		Assert.IsLessThanOrEqualTo(maxExecutionsForOneLoop, executions, "The action ran faster than a single polling loop allows");

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task StopDuringAPendingRestartKeepsTheActionStopped()
	{
		// Arrange
		int counter = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(200),
			ActionInterval = TimeSpan.Zero,
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastStart
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		// Let the loop run its first tick and enter its delay, so the restart below has to wait for it.
		await Task.Delay(50).ConfigureAwait(false);

		// Act: stop while the restart is still waiting for the old loop to finish
		Task restart = intervalAction.RestartAsync();
		Assert.IsFalse(restart.IsCompleted, "The restart should still be waiting on the old loop's delay.");
		intervalAction.Stop();
		await restart.ConfigureAwait(false);

		// Bounded, because the failure this guards against is a loop that never ends.
		Task pollingTask = intervalAction.PollingTask;
		Assert.AreSame(pollingTask, await Task.WhenAny(pollingTask, Task.Delay(1000)).ConfigureAwait(false), "The polling loop should end after Stop, even with a restart pending.");

		int counterAfterStop = Volatile.Read(ref counter);
		await Task.Delay(500).ConfigureAwait(false);

		// Assert: the later Stop wins over the earlier restart
		Assert.AreEqual(counterAfterStop, Volatile.Read(ref counter), "The action should not run after Stop, even with a restart pending.");
		Assert.IsTrue(intervalAction.PollingTask.IsCompleted, "No polling loop should be running after Stop.");

		// A restart requested after the stop still resumes polling.
		await intervalAction.RestartAsync().ConfigureAwait(false);
		await Task.Delay(50).ConfigureAwait(false);
		Assert.IsGreaterThan(counterAfterStop, Volatile.Read(ref counter), "A restart after Stop should resume execution.");

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
		await WaitForPollingToFaultAsync(intervalAction).ConfigureAwait(false);
		// Act
		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);
		Assert.AreEqual(exceptionMessage, exception.Message);
		intervalAction.Stop();
	}

	[TestMethod]
	public async Task RethrowExceptionsKeepsTheActionStackTrace()
	{
		// Arrange
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			Action = ThrowFromNamedMethod,
			IntervalType = IntervalType.FromLastStart
		};
		IntervalAction intervalAction = IntervalAction.Start(options);
		await WaitForPollingToFaultAsync(intervalAction).ConfigureAwait(false);

		// Act
		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);
		intervalAction.Stop();

		// Assert: `throw ex` used to reset the trace so it began at RethrowExceptions
		StringAssert.Contains(exception.StackTrace, nameof(ThrowFromNamedMethod));
	}

	[TestMethod]
	public async Task AsyncActionRunsNeverOverlap()
	{
		// Arrange: each run awaits far longer than the interval between runs
		int running = 0;
		int maxRunning = 0;
		int started = 0;
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromMilliseconds(20),
			AsyncAction = async _ =>
			{
				int nowRunning = Interlocked.Increment(ref running);
				Interlocked.Increment(ref started);
				InterlockedMax(ref maxRunning, nowRunning);
				await Task.Delay(200, CancellationToken.None).ConfigureAwait(false);
				Interlocked.Decrement(ref running);
			},
			IntervalType = IntervalType.FromLastCompletion
		};

		// Act
		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(1000).ConfigureAwait(false);
		intervalAction.Stop();

		// Assert: a run lasts until its task completes, so the next one waits for it
		Assert.IsGreaterThanOrEqualTo(2, Volatile.Read(ref started), "Expected the async action to run more than once.");
		Assert.AreEqual(1, Volatile.Read(ref maxRunning), "Async action runs should never overlap.");

		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public async Task AsyncActionFromLastCompletionMeasuresFromWhenTheWorkFinished()
	{
		// Arrange: record when each run starts and when its awaited work finishes
		TimeSpan actionInterval = TimeSpan.FromMilliseconds(100);
		List<(long Start, long Finish)> runs = [];
		Lock runsLock = new();
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = actionInterval,
			AsyncAction = async _ =>
			{
				long start = System.Diagnostics.Stopwatch.GetTimestamp();
				await Task.Delay(150, CancellationToken.None).ConfigureAwait(false);
				long finish = System.Diagnostics.Stopwatch.GetTimestamp();
				lock (runsLock)
				{
					runs.Add((start, finish));
				}
			},
			IntervalType = IntervalType.FromLastCompletion
		};

		// Act
		IntervalAction intervalAction = IntervalAction.Start(options);
		await Task.Delay(1200).ConfigureAwait(false);
		intervalAction.Stop();
		intervalAction.RethrowExceptions();

		// Assert: each run starts at least an interval after the previous one's work finished
		(long Start, long Finish)[] snapshot;
		lock (runsLock)
		{
			snapshot = [.. runs];
		}

		Assert.IsGreaterThanOrEqualTo(2, snapshot.Length, "Expected at least two completed runs.");
		for (int i = 1; i < snapshot.Length; i++)
		{
			TimeSpan gap = TimeSpan.FromTicks((long)((snapshot[i].Start - snapshot[i - 1].Finish) * ((double)TimeSpan.TicksPerSecond / System.Diagnostics.Stopwatch.Frequency)));
			Assert.IsGreaterThanOrEqualTo(actionInterval, gap, $"Run {i} started {gap.TotalMilliseconds:F0} ms after the previous run finished.");
		}
	}

	[TestMethod]
	public async Task AsyncActionExceptionAfterAwaitIsRethrown()
	{
		// Arrange
		string exceptionMessage = "Thrown after an await";
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.Zero,
			AsyncAction = async _ =>
			{
				await Task.Delay(20, CancellationToken.None).ConfigureAwait(false);
				throw new InvalidOperationException(exceptionMessage);
			},
			IntervalType = IntervalType.FromLastStart
		};

		// Act
		IntervalAction intervalAction = IntervalAction.Start(options);
		await WaitForPollingToFaultAsync(intervalAction).ConfigureAwait(false);

		// Assert: the exception reaches RethrowExceptions instead of the thread pool
		InvalidOperationException exception = Assert.ThrowsExactly<InvalidOperationException>(intervalAction.RethrowExceptions);
		Assert.AreEqual(exceptionMessage, exception.Message);
		intervalAction.Stop();
	}

	[TestMethod]
	public async Task StopCancelsTheAsyncActionToken()
	{
		// Arrange: the action waits on its token for far longer than the test
		using ManualResetEventSlim started = new();
		IntervalActionOptions options = new()
		{
			PollingInterval = TimeSpan.FromMilliseconds(10),
			ActionInterval = TimeSpan.FromHours(1),
			AsyncAction = async cancellationToken =>
			{
				started.Set();
				await Task.Delay(TimeSpan.FromHours(1), cancellationToken).ConfigureAwait(false);
			},
			IntervalType = IntervalType.FromLastCompletion
		};

		IntervalAction intervalAction = IntervalAction.Start(options);
		Assert.IsTrue(started.Wait(TimeSpan.FromSeconds(10)), "The action should start.");
		Task? actionTask = intervalAction.ActionTask;
		Assert.IsNotNull(actionTask, "The action should still be running.");

		// Act
		intervalAction.Stop();

		// Assert: the run ends promptly, and giving up on cancellation is not reported as a failure
		Assert.AreSame(actionTask, await Task.WhenAny(actionTask, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false), "Stop should cancel the running async action.");
		Assert.IsFalse(actionTask.IsFaulted, "A run that ends because Stop cancelled it should not fault.");
		intervalAction.RethrowExceptions();
	}

	[TestMethod]
	public void StartThrowsWhenBothActionAndAsyncActionAreSet()
	{
		IntervalActionOptions options = new()
		{
			Action = () => { },
			AsyncAction = _ => Task.CompletedTask,
		};

		_ = Assert.ThrowsExactly<ArgumentException>(() => IntervalAction.Start(options));
	}

	private static void InterlockedMax(ref int target, int value)
	{
		int current = Volatile.Read(ref target);
		while (value > current)
		{
			int seen = Interlocked.CompareExchange(ref target, value, current);
			if (seen == current)
			{
				return;
			}

			current = seen;
		}
	}

	/// <summary>
	/// Waits until the polling loop has ended, which for an action that throws is when the loop
	/// has observed the fault and <see cref="IntervalAction.RethrowExceptions"/> has something to throw.
	/// </summary>
	/// <param name="intervalAction">The instance whose polling loop is expected to fault.</param>
	/// <returns>A task that completes once the polling loop has ended.</returns>
	/// <remarks>
	/// An exception reaches the polling task in three hops: one tick starts the action on the
	/// thread pool, the action faults, and a later tick sees the faulted task and throws. That is
	/// at least one polling interval plus two thread pool dispatches, none of which a fixed delay
	/// can bound; a slow runner (macOS CI) took longer than the 30 ms the test used to allow.
	/// Waiting on the task itself is exact, and the timeout only turns a hang into a failure.
	/// </remarks>
	private static async Task WaitForPollingToFaultAsync(IntervalAction intervalAction)
	{
		Task pollingTask = intervalAction.PollingTask;
		Task finished = await Task.WhenAny(pollingTask, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
		Assert.AreSame(pollingTask, finished, "The polling loop should fault after the action throws.");
	}

	private static void ThrowFromNamedMethod() => throw new InvalidOperationException("Thrown from a named method");

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
		await WaitForPollingToFaultAsync(intervalAction).ConfigureAwait(false);
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
		await WaitForPollingToFaultAsync(intervalAction).ConfigureAwait(false);
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

	[TestMethod]
	public async Task SettingTheWallClockBackDoesNotDelayTheNextRun()
	{
		int counter = 0;
		IntervalActionOptions options = new()
		{
			// Long enough that the loop runs the action once and then leaves TryRun to the test.
			PollingInterval = TimeSpan.FromHours(1),
			ActionInterval = TimeSpan.FromMilliseconds(50),
			Action = () => Interlocked.Increment(ref counter),
			IntervalType = IntervalType.FromLastCompletion
		};

		IntervalAction intervalAction = IntervalAction.Start(options);

		// Wait for the first run to finish, so it has recorded when it ran. The loop then sits in its
		// hour-long delay, and stopping it now keeps it from ever calling TryRun again.
		DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(10);
		while (intervalAction.ActionTask is not { IsCompleted: true } && DateTimeOffset.Now < deadline)
		{
			await Task.Delay(10).ConfigureAwait(false);
		}

		intervalAction.Stop();
		Assert.AreEqual(1, counter, "Expected the first run to have happened.");

		// This is where a wall clock that has just been set back an hour puts the last run: an hour
		// ahead of now. Only wall-clock time moves when the clock is set, so this is the whole of what
		// a clock change does to the instance.
		intervalAction.LastRunTime = DateTimeOffset.Now.AddHours(1);

		await Task.Delay(options.ActionInterval * 3).ConfigureAwait(false);

		Assert.IsTrue(intervalAction.TryRun(), "Expected the action to run once its interval had passed, whatever the wall clock says.");
	}
}
