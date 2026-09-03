namespace Repl;

/// <summary>
/// Controls whether standalone runs translate process termination signals into cooperative cancellation.
/// </summary>
public enum ProcessSignalHandlingMode
{
	/// <summary>
	/// Standalone runs handle Ctrl+C console events, plus Ctrl+Break on Windows, for their duration.
	/// They also handle SIGTERM on supported Unix platforms. Interactive sessions retain
	/// their existing console command-cancellation behavior.
	/// </summary>
	Automatic = 0,

	/// <summary>
	/// Process signal handling remains the responsibility of the caller.
	/// </summary>
	None = 1,
}
