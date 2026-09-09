namespace Repl;

/// <summary>
/// Internal carrier threaded through the execution pipeline until the exit-code policy is applied once
/// at the top level. Mirrors <see cref="ReplExecutionOutcome"/> minus the resolved code.
/// </summary>
/// <param name="Kind">Outcome category.</param>
/// <param name="Result">Final result object, when one exists.</param>
/// <param name="Exception">Exception that ended the run, when one did.</param>
/// <param name="ExplicitExitCode">
/// Code carried by the outcome itself: the <see cref="IExitResult"/> code, or the conventional signal
/// code for a cancellation/interruption. The table still decides for every other kind.
/// </param>
internal readonly record struct ExecutionOutcome(
	ReplExecutionOutcomeKind Kind,
	object? Result = null,
	Exception? Exception = null,
	int? ExplicitExitCode = null)
{
	public static ExecutionOutcome Success { get; } = new(ReplExecutionOutcomeKind.Success);

	public static ExecutionOutcome Help { get; } = new(ReplExecutionOutcomeKind.Help);

	/// <summary>
	/// A refusal the caller was shown. <paramref name="exception"/> is set only when the usage error
	/// displaced a failure that was already being reported — an unknown output format while rendering a
	/// handler exception — so the cause the run actually ended on is not lost to the resolver.
	/// </summary>
	public static ExecutionOutcome UsageError(object? rendered = null, Exception? exception = null) =>
		new(ReplExecutionOutcomeKind.UsageError, rendered, exception);

	public static ExecutionOutcome BindingError(Exception exception, object? rendered = null) =>
		new(ReplExecutionOutcomeKind.BindingError, rendered, exception);

	public static ExecutionOutcome HandlerError(object? result) => new(ReplExecutionOutcomeKind.HandlerError, result);

	public static ExecutionOutcome HandlerExitCode(IExitResult exitResult) =>
		new(ReplExecutionOutcomeKind.HandlerExitCode, exitResult, ExplicitExitCode: exitResult.ExitCode);

	public static ExecutionOutcome HandlerException(Exception exception) => new(ReplExecutionOutcomeKind.HandlerException, Exception: exception);

	public static ExecutionOutcome Cancelled(Exception exception, int? conventionalExitCode = null) =>
		new(ReplExecutionOutcomeKind.Cancelled, Exception: exception, ExplicitExitCode: conventionalExitCode);

	public static ExecutionOutcome FrameworkError(object? rendered, Exception? exception = null) =>
		new(ReplExecutionOutcomeKind.FrameworkError, rendered, exception);

	/// <summary>
	/// True when the outcome should not prevent an automatic transition into the interactive loop.
	/// </summary>
	public bool IsSuccessLike =>
		Kind is ReplExecutionOutcomeKind.Success or ReplExecutionOutcomeKind.Help
		|| (Kind == ReplExecutionOutcomeKind.HandlerExitCode && ExplicitExitCode == 0);
}
