// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.IntervalAction;

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
	public static IntervalAction Start(IntervalActionOptions intervalActionOptions)
	{
		Ensure.NotNull(intervalActionOptions);
		Ensure.NotNull(intervalActionOptions.Action);

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
	public void Stop()
	{
		lock (Lock)
		{
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
			return RestartGate = RestartCoreAsync(RestartGate);
		}
	}

	/// <summary>
	/// Stops any current polling and starts a fresh polling task, once <paramref name="previousRestart"/> has finished.
	/// </summary>
	/// <param name="previousRestart">The restart this one follows, so that restarts do not interleave.</param>
	/// <returns>A task that represents the asynchronous operation.</returns>
	private async Task RestartCoreAsync(Task previousRestart)
	{
		await WaitAndDiscardOutcomeAsync(previousRestart).ConfigureAwait(false);

		bool shouldPoll;

		lock (Lock)
		{
			shouldPoll = ShouldPoll;
		}

		if (shouldPoll)
		{
			Stop();
			await WaitAndDiscardOutcomeAsync(PollingTask).ConfigureAwait(false);
		}

		lock (Lock)
		{
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

		if (ActionTask?.IsCompleted ?? false)
		{
			if (ActionTask.Exception is not null)
			{
				throw ActionTask.Exception.GetBaseException();
			}

			ActionTask = null;
		}

		DateTimeOffset lastRunTime;

		lock (Lock)
		{
			lastRunTime = LastRunTime;
		}

		if (ActionInterval >= TimeSpan.Zero && ActionTask is null && DateTimeOffset.Now - lastRunTime > ActionInterval)
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

	/// <summary>
	/// Rethrows any exceptions that occurred during the polling task.
	/// </summary>
	public void RethrowExceptions()
	{
		if (PollingTask.Exception is not null)
		{
			throw PollingTask.Exception.GetBaseException();
		}
	}
}
