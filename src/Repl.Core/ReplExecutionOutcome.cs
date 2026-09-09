namespace Repl;

/// <summary>
/// Structured description of how a top-level run ended, handed to <see cref="ExitCodeOptions.Resolver"/>
/// after every framework layer has run.
/// </summary>
/// <param name="Kind">Outcome category.</param>
/// <param name="ExitCode">
/// Exit code selected by the <see cref="ExitCodeOptions"/> table, or the verbatim code of an
/// <see cref="IExitResult"/> when <paramref name="Kind"/> is <see cref="ReplExecutionOutcomeKind.HandlerExitCode"/>.
/// </param>
/// <param name="Result">
/// Final result object when one exists: the normalized handler result, or the <see cref="IReplResult"/>
/// the framework produced for a refusal. Normally that refusal was rendered to the caller; the one
/// exception is a <see cref="ReplExecutionOutcomeKind.UsageError"/> raised *because* rendering failed
/// (an unknown <c>--output</c> format), which carries the diagnostic the caller never saw.
/// </param>
/// <param name="Exception">Exception that ended the run, when the outcome was caused by one.</param>
/// <remarks>
/// The primary constructor is frozen: future members are added as init-only body properties so
/// consumers that construct outcomes (e.g. to unit-test a resolver) keep binary compatibility. Those
/// body properties are not part of the compiler-synthesized <c>Deconstruct</c>, which covers the
/// positional parameters only — read <see cref="Scope"/> by name rather than deconstructing.
/// </remarks>
public sealed record ReplExecutionOutcome(
	ReplExecutionOutcomeKind Kind,
	int ExitCode,
	object? Result = null,
	Exception? Exception = null)
{
	/// <summary>
	/// Gets what this exit code is used for: <see cref="ReplExitCodeScope.Process"/> for the process
	/// exit code of a run (the default, and the only value a one-shot run sees), or
	/// <see cref="ReplExitCodeScope.ShellIntegrationMark"/> when an interactive session is computing one
	/// command's command-end mark. See <see cref="ExitCodeOptions.Resolver"/> for the invocation contract.
	/// </summary>
	public ReplExitCodeScope Scope { get; init; }
}
