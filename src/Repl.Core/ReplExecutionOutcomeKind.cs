namespace Repl;

/// <summary>
/// Classifies how a top-level run ended, independently of the exit code eventually returned.
/// Values are explicit and append-only so consumers can persist or switch on them safely.
/// </summary>
public enum ReplExecutionOutcomeKind
{
	/// <summary>
	/// The handler completed and produced a success-like result (or no result); also an ambient command
	/// (<c>exit</c>, <c>..</c>) that did its job, and a clean interactive session exit.
	/// </summary>
	Success = 0,

	/// <summary>
	/// The invocation rendered help instead of running a command: help request, bare invocation, or
	/// scoped-context help.
	/// </summary>
	Help = 1,

	/// <summary>
	/// The framework refused the invocation before the handler ran: unknown command, ambiguous
	/// prefix, invalid option, unknown output format, or context validation failure.
	/// </summary>
	UsageError = 2,

	/// <summary>
	/// Handler arguments could not be bound: a token failed to convert or was missing, or a value the
	/// binder resolves itself (context value, <c>[FromServices]</c> dependency, typed global options
	/// service) was unavailable.
	/// </summary>
	BindingError = 3,

	/// <summary>
	/// The handler ran and returned a failure result (<c>error</c>, <c>validation</c>, <c>not_found</c>, …).
	/// </summary>
	HandlerError = 4,

	/// <summary>
	/// The handler returned an <see cref="IExitResult"/>; its exit code is used verbatim.
	/// </summary>
	HandlerExitCode = 5,

	/// <summary>
	/// The handler, a middleware, or user code running after binding (validators, banners, output
	/// transformers) threw an exception that the framework reported.
	/// </summary>
	HandlerException = 6,

	/// <summary>
	/// The run ended with an <see cref="OperationCanceledException"/>: typically the caller-supplied
	/// <see cref="CancellationToken"/>, but also a cancelled interactive prompt or a handler that threw it.
	/// </summary>
	Cancelled = 7,

	/// <summary>
	/// The run was interrupted by a process signal (SIGINT, Ctrl+Break, SIGTERM). Reserved for
	/// process-signal bridges; the core pipeline never produces it.
	/// </summary>
	Interrupted = 8,

	/// <summary>
	/// The framework itself failed: incompatible adapter contract or an unsupported hosting capability.
	/// </summary>
	FrameworkError = 9,
}
