namespace Repl;

/// <summary>
/// Controls whether standalone runs translate POSIX termination into cooperative cancellation.
/// </summary>
public enum ProcessSignalHandlingMode
{
	/// <summary>
	/// Standalone <see cref="ReplApp.Run(string[], ReplRunOptions?)"/> and
	/// <see cref="ReplApp.RunAsync(string[], ReplRunOptions?, CancellationToken)"/> calls
	/// handle SIGTERM for the duration of the run on Unix platforms.
	/// </summary>
	Automatic = 0,

	/// <summary>
	/// Process signal handling remains the responsibility of the caller.
	/// </summary>
	None = 1,
}
