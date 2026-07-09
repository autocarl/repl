using System.Text.Json;

namespace Repl.Mcp;

internal static class McpJsonStringOutput
{
	public static string UnwrapJsonStringLiteral(string text)
	{
		if (text.Length == 0 || text[0] != '"')
		{
			return text;
		}

		try
		{
			return JsonSerializer.Deserialize(text, McpJsonContext.Default.String) ?? text;
		}
		catch (JsonException)
		{
			return text;
		}
	}
}
