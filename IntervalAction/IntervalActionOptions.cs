// Copyright (c) 2023-2026 ktsu-dev contributors

namespace ktsu.IntervalAction;

/// <summary>
/// Specifies the type of interval for the <see cref="IntervalAction"/>.
/// </summary>
public enum IntervalType
{
	/// <summary>
	/// The interval is measured from the last completion time of the action.
	/// </summary>
	FromLastCompletion,

	/// <summary>
	/// The interval is measured from the last start time of the action.
	/// </summary>
	FromLastStart,
}

/// <summary>
/// Options for configuring an <see cref="IntervalAction"/>.
/// </summary>
public class IntervalActionOptions
{
	/// <summary>
	/// The polling interval for checking the action's status and attempting to start the action.
	/// Default is 1 second. Decreasing this value will increase the frequency of checks and the responsiveness of the action at the cost of performance.
	/// </summary>
	public TimeSpan PollingInterval { get; init; } = TimeSpan.FromSeconds(1);

	/// <summary>
	/// The interval at which the action should be executed.
	/// </summary>
	public TimeSpan ActionInterval { get; init; } = TimeSpan.Zero;

	/// <summary>
	/// The action to be executed at each interval.
	/// </summary>
	public Action Action { get; init; } = () => { };

	/// <summary>
	/// The type of interval measurement for the action, either from the last completion or start time of the action.
	/// Default is <see cref="IntervalType.FromLastCompletion"/>.
	///
	/// NOTE: Tasks will not be started if the previous task is still running to prevent overlapping executions.
	/// If the task is still running when the interval is reached, the new task will be started on the next polling interval after the previous task completes.
	/// </summary>
	public IntervalType IntervalType { get; init; }
}
