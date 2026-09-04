namespace Repl;

/// <summary>
/// Describes whether a console cancel-key dispatch had an owner and whether that owner claimed the key.
/// Callers only ever branch on <see cref="SuppressProcessTermination"/>; <see cref="AllowProcessTermination"/>
/// is distinct from <see cref="NotHandled"/> so that tests can tell an intentional second-signal
/// fall-through from the absence of any active owner.
/// </summary>
internal enum ConsoleCancelKeyHandlingResult
{
	NotHandled,
	SuppressProcessTermination,
	AllowProcessTermination,
}
