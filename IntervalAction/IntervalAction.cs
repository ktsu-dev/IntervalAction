// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.IntervalAction;

using System.Runtime.ExceptionServices;

/// <summary>
/// Represents an action that is executed at specified intervals.
/// Provides options for the interval type, which can be measured from the last completion or start time of the action.
///
/// NOTE: Tasks will not be started if the previous task is still running to prevent overlapping executions.
/// If the task is still running when the interval is reached, the new task will be started on the next TryRun call after the previous task completes.
/// </summary>
public class IntervalAction
{
	/// <summary>
	/// Gets or sets the last run time of the action.
	/// </summary>
	internal DateTimeOffset LastRunTime { get; set; } = DateTimeOffset.MinValue;

	/// <summary>
	/// Gets the polling interval for checking the action's status and attempting to start the action.
	/// </summary>
	private TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// Gets or sets the task responsible for polling the action's status.
	/// </summary>
	internal Task PollingTask { get; set; } = Task.CompletedTask;

	/// <summary>
	/// Gets or sets the task representing the action being executed.
	/// </summary>
	internal Task? ActionTask { get; set; }

	/// <summary>
	/// Gets or sets a value indicating whether polling should continue.
	/// </summary>
	internal bool ShouldPoll { get; set; }

	/// <summary>
	/// Gets the action to be executed at each interval.
	/// </summary>
	private Action Action { get; init; } = null!;

	/// <summary>
	/// Gets the interval at which the action should be executed.
	/// </summary>
	private TimeSpan ActionInterval { get; init; }

	/// <summary>
	/// Gets the type of interval measurement for the action, either from the last completion or start time of the action.
	/// </summary>
	private IntervalType IntervalType { get; init; }

	private Lock Lock { get; } = new();

	/// <summary>
	/// Gets or sets the most recent restart, which the next restart waits on before it begins.
	/// </summary>
	/// <remarks>
	/// <see cref="RestartAsync"/> reads <see cref="ShouldPoll"/>, stops, awaits the old polling task
	/// and only then assigns a new one, re-taking <see cref="Lock"/> at each step rather than holding
	/// it across the awaits. Two callers could therefore pass the same checks and each start a polling
	/// loop, leaving the first running unreferenced against the same <see cref="ActionTask"/> field —
	/// which is the overlap the class documents that it prevents. Chaining restarts through this task
	/// serializes them without holding a lock over an await.
	/// </remarks>
	private Task RestartGate { get; set; } = Task.CompletedTask;

	/// <summary>
	/// Gets or sets how many times <see cref="Stop"/> has been called.
	/// </summary>
	/// <remarks>
	/// A restart records this when it is requested and re-enables polling only if it is unchanged
	/// once the restart's awaits have finished. Without it, a <see cref="Stop"/> that arrives while a
	/// <see cref="RestartAsync"/> is still waiting would be overwritten when the restart sets
	/// <see cref="ShouldPoll"/>, and the action would keep running after the caller stopped it.
	/// </remarks>
	private long StopGeneration { get; set; }

	/// <summary>
	/// Waits for a task to finish and discards however it finished.
	/// </summary>
	/// <param name="task">The task to wait on.</param>
	/// <returns>A task that completes when <paramref name="task"/> has, and never faults.</returns>
	/// <remarks>
	/// Awaiting a faulted task rethrows its exception. A previous polling loop that faulted — which is
	/// what an exception from the user's <see cref="Action"/> leaves behind — is replaceable exactly
	/// like one that ended normally, so a restart observes the fault and moves on instead.
	/// </remarks>
	private static Task WaitAndDiscardOutcomeAsync(Task task) =>
		task.ContinueWith(
			static finished => { _ = finished.Exception; },
			CancellationToken.None,
			TaskContinuationOptions.None,
			TaskScheduler.Default);

	/// <summary>
	/// Initializes a new instance of the <see cref="IntervalAction"/> class.
	/// Don't use this constructor. Use <see cref="Start(IntervalActionOptions)"/> instead.
	/// </summary>
	private IntervalAction() { }

	/// <summary>
	/// Starts a new <see cref="IntervalAction"/> with the specified options.
	/// </summary>
	/// <param name="intervalActionOptions">The options for configuring the interval action.</param>
	/// <returns>A new instance of <see cref="IntervalAction"/>.</returns>
	/// <exception cref="ArgumentNullException">Thrown if <paramref name="intervalActionOptions"/> or its <see cref="IntervalActionOptions.Action"/> is null.</exception>
	/// <exception cref="ArgumentOutOfRangeException">
	/// Thrown if <see cref="IntervalActionOptions.PollingInterval"/> is zero or negative, which includes
	/// <see cref="Timeout.InfiniteTimeSpan"/>.
	/// </exception>
	public static IntervalAction Start(IntervalActionOptions intervalActionOptions)
	{
		Ensure.NotNull(intervalActionOptions);
		Ensure.NotNull(intervalActionOptions.Action);

		// Rejected here rather than left to Task.Delay in the polling loop: a negative interval would
		// fault the loop after the first run, an infinite one would leave Restart() and Stop() waiting
		// on a delay that never ends, and zero would spin a core.
		if (intervalActionOptions.PollingInterval <= TimeSpan.Zero)
		{
			throw new ArgumentOutOfRangeException(
				nameof(intervalActionOptions),
				intervalActionOptions.PollingInterval,
				$"{nameof(IntervalActionOptions.PollingInterval)} must be greater than zero.");
		}

		IntervalAction intervalAction = new()
		{
			PollingInterval = intervalActionOptions.PollingInterval,
			Action = intervalActionOptions.Action,
			ActionInterval = intervalActionOptions.ActionInterval,
			IntervalType = intervalActionOptions.IntervalType
		};

		intervalAction.Restart();

		return intervalAction;
	}

	/// <summary>
	/// Stops the polling of the action.
	/// </summary>
	/// <remarks>
	/// A restart that is still pending when this is called is cancelled rather than left to resume
	/// polling once it finishes. A restart requested after this call is unaffected.
	/// </remarks>
	public void Stop()
	{
		lock (Lock)
		{
			StopGeneration++;
			ShouldPoll = false;
		}
	}

	/// <summary>
	/// Restarts the polling of the action.
	/// </summary>
	public void Restart() => RestartAsync().Wait();

	/// <summary>
	/// Asynchronously restarts the polling of the action.
	/// </summary>
	/// <returns>A task that represents the asynchronous operation.</returns>
	public Task RestartAsync()
	{
		lock (Lock)
		{
			return RestartGate = RestartCoreAsync(RestartGate, StopGeneration);
		}
	}

	/// <summary>
	/// Stops any current polling and starts a fresh polling task, once <paramref name="previousRestart"/> has finished.
	/// </summary>
	/// <param name="previousRestart">The restart this one follows, so that restarts do not interleave.</param>
	/// <param name="stopGeneration">
	/// The value of <see cref="StopGeneration"/> when the restart was requested. If a
	/// <see cref="Stop"/> has happened since, the restart ends without starting a new loop.
	/// </param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task RestartCoreAsync(Task previousRestart, long stopGeneration)
	{
		await WaitAndDiscardOutcomeAsync(previousRestart).ConfigureAwait(false);

		// Wait for the old loop whether or not it is still meant to be polling. After Stop() it can
		// still be inside its delay, and would see ShouldPoll set again below and keep running
		// alongside the new loop, unreferenced. This clears ShouldPoll directly rather than calling
		// Stop(), which would count as a caller's stop and cancel this very restart.
		lock (Lock)
		{
			ShouldPoll = false;
		}

		await WaitAndDiscardOutcomeAsync(PollingTask).ConfigureAwait(false);

		lock (Lock)
		{
			if (StopGeneration != stopGeneration)
			{
				return;
			}

			ShouldPoll = true;

			// A faulted action task is left in place by TryRun, which throws rather than clearing it.
			// The new loop's first tick would observe it again and fault too, so the restart would
			// have replaced one dead loop with another. Only a task that has finished is cleared: an
			// action still running keeps its slot, which is what stops the new loop overlapping it.
			if (ActionTask?.IsCompleted ?? false)
			{
				_ = ActionTask.Exception;
				ActionTask = null;
			}
		}

		PollingTask = Task.Run(async () =>
		{
			bool shouldPoll;

			lock (Lock)
			{
				shouldPoll = ShouldPoll;
			}

			while (shouldPoll)
			{
				TryRun();
				await Task.Delay(PollingInterval).ConfigureAwait(false);

				lock (Lock)
				{
					shouldPoll = ShouldPoll;
				}
			}
		});
	}

	/// <summary>
	/// Attempts to run the action if the specified interval has passed since the last execution.
	/// </summary>
	/// <returns><c>true</c> if the action was started; otherwise, <c>false</c>.</returns>
	internal bool TryRun()
	{
		Ensure.NotNull(Action);

		// Check and claim ActionTask under the lock, so two callers can never both see it empty and
		// each start the action
		lock (Lock)
		{
			if (ActionTask?.IsCompleted ?? false)
			{
				if (ActionTask.Exception is not null)
				{
					// Rethrow through ExceptionDispatchInfo so the trace still shows where the action failed
					ExceptionDispatchInfo.Capture(ActionTask.Exception.GetBaseException()).Throw();
				}

				ActionTask = null;
			}

			if (ActionInterval >= TimeSpan.Zero && ActionTask is null && DateTimeOffset.Now - LastRunTime > ActionInterval)
			{
				ActionTask = Task.Run(() =>
				{
					if (IntervalType == IntervalType.FromLastStart)
					{
						lock (Lock)
						{
							LastRunTime = DateTimeOffset.Now;
						}
					}

					Action();

					if (IntervalType == IntervalType.FromLastCompletion)
					{
						lock (Lock)
						{
							LastRunTime = DateTimeOffset.Now;
						}
					}
				});

				return true;
			}

			return false;
		}
	}

	/// <summary>
	/// Rethrows any exceptions that occurred during the polling task.
	/// </summary>
	public void RethrowExceptions()
	{
		if (PollingTask.Exception is not null)
		{
			ExceptionDispatchInfo.Capture(PollingTask.Exception.GetBaseException()).Throw();
		}
	}
}
