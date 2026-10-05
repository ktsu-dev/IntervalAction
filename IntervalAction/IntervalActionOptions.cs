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
	/// The default <see cref="Action"/>, which does nothing. Kept as one instance so that
	/// <see cref="IntervalAction.Start(IntervalActionOptions)"/> can tell an <see cref="Action"/>
	/// that was left alone from one that was set alongside <see cref="AsyncAction"/>.
	/// </summary>
	internal static Action NoAction { get; } = () => { };

	/// <summary>
	/// The action to be executed at each interval.
	/// </summary>
	/// <remarks>
	/// Do not assign an async lambda to this property: it compiles to <c>async void</c>, which returns
	/// at its first <see langword="await"/>. The run then looks finished while its work continues,
	/// so runs overlap, <see cref="IntervalType.FromLastCompletion"/> measures from the first await,
	/// and an exception after the await crashes the process. Use <see cref="AsyncAction"/> instead.
	/// </remarks>
	public Action Action { get; init; } = NoAction;

	/// <summary>
	/// The asynchronous action to be executed at each interval, in place of <see cref="Action"/>.
	/// </summary>
	/// <remarks>
	/// A run lasts until the returned task completes, so runs never overlap,
	/// <see cref="IntervalType.FromLastCompletion"/> measures from when the work actually finished, and
	/// an exception thrown after an <see langword="await"/> is reported by
	/// <see cref="IntervalAction.RethrowExceptions"/> like one from a synchronous action. The token is
	/// cancelled when <see cref="IntervalAction.Stop"/> is called; a run that ends by throwing
	/// <see cref="OperationCanceledException"/> because of it is treated as a normal completion.
	/// Set either this or <see cref="Action"/>, not both.
	/// </remarks>
	public Func<CancellationToken, Task>? AsyncAction { get; init; }

	/// <summary>
	/// The type of interval measurement for the action, either from the last completion or start time of the action.
	/// Default is <see cref="IntervalType.FromLastCompletion"/>.
	///
	/// NOTE: Tasks will not be started if the previous task is still running to prevent overlapping executions.
	/// If the task is still running when the interval is reached, the new task will be started on the next polling interval after the previous task completes.
	/// </summary>
	public IntervalType IntervalType { get; init; }
}
