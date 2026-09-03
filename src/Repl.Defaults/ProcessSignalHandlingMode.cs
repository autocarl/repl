namespace Repl;

/// <summary>
/// Controls whether standalone runs translate process termination signals into cooperative cancellation.
/// </summary>
public enum ProcessSignalHandlingMode
{
	/// <summary>
	/// Standalone <see cref="ReplApp.Run(string[], ReplRunOptions?)"/> and
	/// <see cref="ReplApp.RunAsync(string[], ReplRunOptions?, CancellationToken)"/> calls
	/// handle Ctrl+C/SIGINT for the duration of the run and also handle SIGTERM on Unix platforms. Interactive sessions retain their existing Ctrl+C behavior.
	/// </summary>
	Automatic = 0,

	/// <summary>
	/// Process signal handling remains the responsibility of the caller.
	/// </summary>
	None = 1,
}
