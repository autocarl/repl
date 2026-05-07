using Repl.Terminal;

namespace Repl.Tests;

[TestClass]
public sealed class Given_TerminalSurfaceHost
{
	[TestMethod]
	[Description("Terminal surface enters alternate screen and restores cursor/wrap/screen when disposed.")]
	public async Task When_AlternateScreenSurfaceIsDisposed_Then_TerminalStateIsRestored()
	{
		using var writer = new StringWriter();

		await using (await TerminalSurfaceHost.EnterAsync(
				writer,
				TerminalSurfaceMode.AlternateScreen,
				CancellationToken.None).ConfigureAwait(false))
		{
			await writer.WriteAsync("body").ConfigureAwait(false);
		}

		var output = writer.ToString();
		output.Should().Contain(AnsiSequences.EnterAlternateScreen);
		output.Should().Contain(AnsiSequences.HideCursor);
		output.Should().Contain(AnsiSequences.DisableLineWrap);
		output.Should().Contain(AnsiSequences.CursorHome);
		output.Should().Contain(AnsiSequences.ClearToEndOfScreen);
		output.Should().Contain(AnsiSequences.EnableLineWrap);
		output.Should().Contain(AnsiSequences.ShowCursor);
		output.Should().Contain(AnsiSequences.LeaveAlternateScreen);
		output.IndexOf(AnsiSequences.EnterAlternateScreen, StringComparison.Ordinal)
			.Should().BeLessThan(output.IndexOf("body", StringComparison.Ordinal));
		output.IndexOf("body", StringComparison.Ordinal)
			.Should().BeLessThan(output.IndexOf(AnsiSequences.LeaveAlternateScreen, StringComparison.Ordinal));
	}

	[TestMethod]
	[Description("Terminal inline surface owns the current region without entering the alternate screen.")]
	public async Task When_InlineSurfaceIsDisposed_Then_AlternateScreenIsNotUsed()
	{
		using var writer = new StringWriter();

		await using (await TerminalSurfaceHost.EnterAsync(
				writer,
				TerminalSurfaceMode.InlineRegion,
				CancellationToken.None).ConfigureAwait(false))
		{
			await writer.WriteAsync("body").ConfigureAwait(false);
		}

		var output = writer.ToString();
		output.Should().NotContain(AnsiSequences.EnterAlternateScreen);
		output.Should().NotContain(AnsiSequences.LeaveAlternateScreen);
		output.Should().Contain(AnsiSequences.HideCursor);
		output.Should().Contain(AnsiSequences.DisableLineWrap);
		output.Should().Contain(AnsiSequences.EnableLineWrap);
		output.Should().Contain(AnsiSequences.ShowCursor);
	}

	[TestMethod]
	[Description("Terminal surface restores cursor/wrap/alternate screen even when rendering fails.")]
	public async Task When_SurfaceRenderingThrows_Then_TerminalStateIsRestored()
	{
		using var writer = new StringWriter();

		var act = async () =>
		{
			var surface = await TerminalSurfaceHost.EnterAsync(
					writer,
					TerminalSurfaceMode.AlternateScreen,
					CancellationToken.None)
				.ConfigureAwait(false);
			try
			{
				throw new InvalidOperationException("boom");
			}
			finally
			{
				await surface.DisposeAsync().ConfigureAwait(false);
			}
		};

		await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
		var output = writer.ToString();
		output.Should().Contain(AnsiSequences.EnableLineWrap);
		output.Should().Contain(AnsiSequences.ShowCursor);
		output.Should().Contain(AnsiSequences.LeaveAlternateScreen);
	}
}
