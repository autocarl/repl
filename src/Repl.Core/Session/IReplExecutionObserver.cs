namespace Repl;

internal interface IReplExecutionObserver
{
	void OnResult(object? result);

	void OnInteractionEvent(ReplInteractionEvent evt);

	/// <summary>
	/// Reports how a run ended, after the exit-code policy has resolved its process exit code.
	/// Not raised for per-command shell-integration marks or for nested sub-invocations. Only the
	/// kind is reported: the resolved code is what <c>RunAsync</c> returns, and the one consumer
	/// needs to distinguish an interrupted run from a completed one.
	/// </summary>
	void OnOutcome(ReplExecutionOutcomeKind kind);
}
