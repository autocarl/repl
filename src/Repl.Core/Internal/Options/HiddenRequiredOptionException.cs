namespace Repl.Internal.Options;

/// <summary>
/// Raised when discovery would hide an option that the active invocation contract cannot omit.
/// </summary>
internal sealed class HiddenRequiredOptionException : InvalidOperationException
{
	internal HiddenRequiredOptionException(string parameterName, string? renderedToken, string route)
		: base(BuildMessage(parameterName, renderedToken, route))
	{
	}

	private static string BuildMessage(string parameterName, string? renderedToken, string route)
	{
		var rendered = renderedToken is null ? string.Empty : $" (rendered as '{renderedToken}')";
		return $"Option target '{parameterName}'{rendered} for command '{route}' cannot be hidden because it is required. "
			+ "Either drop the Required/Arity constraint on the option, or hide a different one.";
	}
}
