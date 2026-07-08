using System.Text;
using Repl.Spectre;

namespace Repl.SpectreTests;

[TestClass]
public sealed class Given_SessionAnsiConsole
{
	[TestMethod]
	[Description("Unicode-capable encodings pass the box-drawing trial-encode, so Spectre keeps its rounded borders on UTF sinks.")]
	public void When_EncodingCarriesUnicode_Then_BoxDrawingIsRenderable()
	{
		SessionAnsiConsole.CanRenderBoxDrawing(Encoding.UTF8).Should().BeTrue();
		SessionAnsiConsole.CanRenderBoxDrawing(Encoding.Unicode).Should().BeTrue();
	}

	[TestMethod]
	[Description("Legacy encodings whose fallback turns box-drawing glyphs into '?' fail the trial-encode, so Spectre falls back to ASCII-safe borders instead of shipping mojibake.")]
	public void When_EncodingCannotCarryBoxDrawing_Then_FallbackIsDetected()
	{
		SessionAnsiConsole.CanRenderBoxDrawing(Encoding.ASCII).Should().BeFalse();
		SessionAnsiConsole.CanRenderBoxDrawing(Encoding.Latin1).Should().BeFalse();
	}
}
