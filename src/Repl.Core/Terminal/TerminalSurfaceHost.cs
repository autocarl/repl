namespace Repl.Terminal;

internal static class TerminalSurfaceHost
{
	public static async ValueTask<TerminalSurfaceScope> EnterAsync(
		TextWriter output,
		TerminalSurfaceMode mode,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(output);

		if (mode == TerminalSurfaceMode.AlternateScreen)
		{
			await output.WriteAsync(AnsiSequences.EnterAlternateScreen).ConfigureAwait(false);
		}

		await output.WriteAsync(AnsiSequences.HideCursor).ConfigureAwait(false);
		await output.WriteAsync(AnsiSequences.DisableLineWrap).ConfigureAwait(false);
		if (mode == TerminalSurfaceMode.AlternateScreen)
		{
			await output.WriteAsync(AnsiSequences.CursorHome).ConfigureAwait(false);
		}

		await output.WriteAsync(AnsiSequences.ClearToEndOfScreen).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);

		return new TerminalSurfaceScope(output, mode);
	}
}
