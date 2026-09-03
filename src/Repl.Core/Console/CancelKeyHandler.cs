namespace Repl;

/// <summary>
/// Session-scoped Ctrl+C handler that implements double-tap cancellation.
/// <list type="bullet">
///   <item>1st Ctrl+C during a command → cancels the per-command CTS, session continues.</item>
///   <item>2nd Ctrl+C within ~2 s (or Ctrl+C with no active command) → exits the process.</item>
/// </list>
/// Uses <see cref="Console.CancelKeyPress"/> which works universally across terminals,
/// IDEs (Rider, VS Code), SSH sessions, and tmux — unlike Esc-key polling.
/// </summary>
internal sealed class CancelKeyHandler : IDisposable
{
	private static readonly TimeSpan DoubleTapWindow = TimeSpan.FromSeconds(2);
	private static int s_activeConsoleHandlers;

	private CancellationTokenSource? _commandCts;
	private DateTimeOffset _lastCancelPress;
	private readonly Lock _lock = new();
	private readonly bool _hooked;
	private int _disposed;

	// Standalone signal scopes yield Ctrl+C while an interactive console handler owns its process-wide semantics.
	internal static bool HasActiveConsoleHandler => Volatile.Read(ref s_activeConsoleHandlers) != 0;

	internal CancelKeyHandler()
	{
		_hooked = !ReplSessionIO.IsSessionActive;
		if (_hooked)
		{
			// Publish ownership before subscribing so an outer standalone handler never claims the same key press.
			Interlocked.Increment(ref s_activeConsoleHandlers);
			try
			{
				Console.CancelKeyPress += OnCancelKeyPress;
			}
			catch
			{
				Interlocked.Decrement(ref s_activeConsoleHandlers);
				throw;
			}
		}
	}

	/// <summary>
	/// Activates per-command cancellation. While active, the first Ctrl+C cancels
	/// this CTS instead of terminating the process.
	/// </summary>
	internal void SetCommandCts(CancellationTokenSource? cts)
	{
		lock (_lock)
		{
			_commandCts = cts;
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		if (_hooked)
		{
			Console.CancelKeyPress -= OnCancelKeyPress;
			Interlocked.Decrement(ref s_activeConsoleHandlers);
		}
	}

	private void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
	{
		lock (_lock)
		{
			var now = DateTimeOffset.UtcNow;

			if (_commandCts is { IsCancellationRequested: false })
			{
				// First Ctrl+C during a command → cancel command, keep session alive.
				e.Cancel = true;
				_commandCts.Cancel();
				_lastCancelPress = now;
				ReplSessionIO.Error.WriteLine();
				ReplSessionIO.Error.WriteLine("Press Ctrl+C again to exit.");
				return;
			}

			if (now - _lastCancelPress < DoubleTapWindow)
			{
				// Second Ctrl+C within window → exit (don't set e.Cancel).
				return;
			}

			// Ctrl+C with no active command → exit.
		}
	}
}
