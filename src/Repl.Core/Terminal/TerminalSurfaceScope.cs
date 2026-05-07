namespace Repl.Terminal;

internal sealed class TerminalSurfaceScope(TextWriter output, TerminalSurfaceMode mode) : IAsyncDisposable
{
	private bool _disposed;

	public TextWriter Output { get; } = output;

	public TerminalSurfaceMode Mode { get; } = mode;

	public ValueTask MoveHomeAsync() =>
		WriteAsync(AnsiSequences.CursorHome);

	public async ValueTask MoveCursorUpAsync(int rows)
	{
		if (rows <= 0)
		{
			return;
		}

		await Output.WriteAsync(AnsiSequences.CursorUp(rows)).ConfigureAwait(false);
	}

	public ValueTask MoveToColumnStartAsync() =>
		WriteAsync('\r');

	public ValueTask ClearToEndOfScreenAsync() =>
		WriteAsync(AnsiSequences.ClearToEndOfScreen);

	public ValueTask FlushAsync(CancellationToken cancellationToken) =>
		new(Output.FlushAsync(cancellationToken));

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		await Output.WriteAsync(AnsiSequences.EnableLineWrap).ConfigureAwait(false);
		await Output.WriteAsync(AnsiSequences.ShowCursor).ConfigureAwait(false);
		if (Mode == TerminalSurfaceMode.AlternateScreen)
		{
			await Output.WriteAsync(AnsiSequences.LeaveAlternateScreen).ConfigureAwait(false);
		}

		await Output.FlushAsync().ConfigureAwait(false);
	}

	private ValueTask WriteAsync(string value) =>
		new(Output.WriteAsync(value));

	private ValueTask WriteAsync(char value) =>
		new(Output.WriteAsync(value));
}
