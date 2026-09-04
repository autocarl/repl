namespace Repl;

/// <summary>
/// Controls whether standalone runs translate process termination signals into cooperative cancellation.
/// </summary>
public enum ProcessSignalHandlingMode
{
	/// <summary>
	/// Process signal handling remains the responsibility of the caller. This is the zero value so that
	/// an unset configuration field or a zero-initialized value agrees with the caller-owned application
	/// default instead of silently claiming process-wide signal ownership.
	/// </summary>
	None = 0,

	/// <summary>
	/// Standalone runs handle Ctrl+C console events, plus Ctrl+Break on Windows, for their duration.
	/// They also handle SIGTERM on supported Unix platforms. Interactive sessions retain
	/// their existing console command-cancellation behavior.
	/// </summary>
	Automatic = 1,
}
