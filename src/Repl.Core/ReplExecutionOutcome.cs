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
/// the framework rendered for a refusal.
/// </param>
/// <param name="Exception">Exception that ended the run, when the outcome was caused by one.</param>
/// <remarks>
/// The primary constructor is frozen: future members are added as init-only body properties so
/// consumers that construct outcomes (e.g. to unit-test a resolver) keep binary compatibility.
/// </remarks>
public sealed record ReplExecutionOutcome(
	ReplExecutionOutcomeKind Kind,
	int ExitCode,
	object? Result = null,
	Exception? Exception = null);
