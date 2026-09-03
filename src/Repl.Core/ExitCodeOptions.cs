namespace Repl;

/// <summary>
/// Maps <see cref="ReplExecutionOutcomeKind"/> categories to process exit codes and exposes a final
/// interception point. Applies to top-level runs only; nested sub-invocations use the built-in defaults.
/// </summary>
public sealed class ExitCodeOptions
{
	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.Success"/>. Default <c>0</c>.
	/// </summary>
	public int Success { get; set; }

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.Help"/>. Default <c>0</c>;
	/// set it non-zero when a bare invocation must fail in scripted pipelines.
	/// </summary>
	public int Help { get; set; }

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.UsageError"/>. Default <c>2</c>.
	/// </summary>
	public int UsageError { get; set; } = 2;

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.BindingError"/>. Default <c>2</c>.
	/// </summary>
	public int BindingError { get; set; } = 2;

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.HandlerError"/>. Default <c>1</c>.
	/// </summary>
	public int HandlerError { get; set; } = 1;

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.HandlerException"/>. Default <c>1</c>.
	/// </summary>
	public int HandlerException { get; set; } = 1;

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.Cancelled"/>. When <see langword="null"/>
	/// (the default) the <see cref="OperationCanceledException"/> propagates to the caller instead of being
	/// converted; <c>130</c> (128 + SIGINT) is the usual shell convention.
	/// </summary>
	public int? Cancelled { get; set; }

	/// <summary>
	/// Gets or sets the exit code for <see cref="ReplExecutionOutcomeKind.FrameworkError"/>. Default <c>1</c>.
	/// </summary>
	public int FrameworkError { get; set; } = 1;

	/// <summary>
	/// Gets or sets a final interception hook invoked with the structured outcome (whose
	/// <see cref="ReplExecutionOutcome.ExitCode"/> already reflects this table); its return value becomes
	/// the process exit code. Invoked once per one-shot run. In an interactive session it is also invoked
	/// once per committed command to compute the shell-integration command-end mark, and once more when
	/// the session exits.
	/// </summary>
	public Func<ReplExecutionOutcome, int>? Resolver { get; set; }

	// Kept private so no friend assembly can mutate the process-wide defaults used by sub-invocations.
	private static readonly ExitCodeOptions s_defaults = new();

	/// <summary>
	/// Maps a kind with the built-in defaults, ignoring any application configuration.
	/// </summary>
	internal static int MapDefault(ReplExecutionOutcomeKind kind, int? carriedExitCode) =>
		s_defaults.Map(kind, carriedExitCode);

	/// <summary>
	/// Maps a kind to its configured code. <paramref name="carriedExitCode"/> is the code the outcome
	/// itself carries: the <see cref="IExitResult"/> code, or the conventional signal code (130/143) for
	/// cancellation and interruption, which <see cref="Cancelled"/> overrides when set.
	/// </summary>
	internal int Map(ReplExecutionOutcomeKind kind, int? carriedExitCode) =>
		kind switch
		{
			ReplExecutionOutcomeKind.Success => Success,
			ReplExecutionOutcomeKind.Help => Help,
			ReplExecutionOutcomeKind.UsageError => UsageError,
			ReplExecutionOutcomeKind.BindingError => BindingError,
			ReplExecutionOutcomeKind.HandlerError => HandlerError,
			ReplExecutionOutcomeKind.HandlerExitCode => carriedExitCode ?? Success,
			ReplExecutionOutcomeKind.HandlerException => HandlerException,
			ReplExecutionOutcomeKind.Cancelled => Cancelled ?? carriedExitCode ?? FrameworkError,
			ReplExecutionOutcomeKind.Interrupted => carriedExitCode ?? FrameworkError,
			_ => FrameworkError,
		};
}
