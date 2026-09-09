namespace Repl;

internal interface IReplExecutionObserver
{
	void OnResult(object? result);

	void OnInteractionEvent(ReplInteractionEvent evt);

	/// <summary>
	/// Reports how a run ended, once the exit-code policy has resolved its process exit code.
	/// Not raised for per-command shell-integration marks or for nested sub-invocations.
	/// Defaulted so an observer that only cares about results needs no change.
	/// </summary>
	void OnOutcome(ReplExecutionOutcomeKind kind, int exitCode)
	{
	}
}
