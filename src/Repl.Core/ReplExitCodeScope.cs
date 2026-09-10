namespace Repl;

/// <summary>
/// What an exit code computed through <see cref="ExitCodeOptions"/> is used for. Values are explicit
/// and append-only so consumers can switch on them safely.
/// </summary>
public enum ReplExitCodeScope
{
	/// <summary>
	/// The code the process exits with: one per top-level run, decided after every framework layer has
	/// run. The default, and the only value a one-shot run ever observes.
	/// </summary>
	Process = 0,

	/// <summary>
	/// The code decorating one interactive command's shell-integration command-end mark
	/// (OSC 133/633 <c>D;&lt;code&gt;</c>). Scoped to that command; the process exit is resolved
	/// separately when the session ends.
	/// </summary>
	ShellIntegrationMark = 1,
}
