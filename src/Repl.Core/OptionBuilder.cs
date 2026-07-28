namespace Repl;

/// <summary>
/// Configures discovery metadata for an option.
/// </summary>
public sealed class OptionBuilder
{
	private readonly Action<bool> _setHidden;
	private readonly Action<bool> _setAutomationHidden;
	private readonly Action<string, bool> _setAliasHidden;

	internal OptionBuilder(
		Action<bool> setHidden,
		Action<bool> setAutomationHidden,
		Action<string, bool> setAliasHidden)
	{
		_setHidden = setHidden;
		_setAutomationHidden = setAutomationHidden;
		_setAliasHidden = setAliasHidden;
	}

	/// <summary>
	/// Hides or shows the option on every discovery surface: help, interactive and shell completion
	/// including value providers, exported documentation, and generated MCP tool and prompt schemas.
	/// </summary>
	/// <remarks>
	/// Parsing and binding are unaffected, so the option still binds when a command-line or REPL
	/// caller supplies it explicitly. An MCP <c>tools/call</c> that supplies it is rejected, because
	/// the option is absent from the advertised tool schema. A hidden option must be optional. For an
	/// options-group property this fails immediately, here; a direct handler parameter may still be
	/// satisfiable from DI or a synthesized progress channel, which is only knowable once a real
	/// service provider exists, so that case instead fails the first time discovery runs against one
	/// (an explicit documentation request or MCP's aggregate <c>tools/list</c>). This is a discovery
	/// filter, <b>not</b> an access-control boundary: the token remains an invocable part of the
	/// command line for anyone who knows it.
	/// </remarks>
	/// <param name="isHidden">Whether the option is hidden.</param>
	/// <returns>The same builder instance.</returns>
	public OptionBuilder Hidden(bool isHidden = true)
	{
		_setHidden(isHidden);

		return this;
	}

	/// <summary>
	/// Hides or shows one registered alias without changing parsing or the visibility of the
	/// canonical option token.
	/// </summary>
	/// <param name="alias">A full alias token already declared on the option.</param>
	/// <param name="isHidden">Whether the alias is hidden from discovery.</param>
	/// <returns>The same builder instance.</returns>
	public OptionBuilder HiddenAlias(string alias, bool isHidden = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(alias);
		_setAliasHidden(alias, isHidden);
		return this;
	}

	/// <summary>
	/// Hides or shows the option on programmatic surfaces only.
	/// </summary>
	/// <remarks>
	/// Unlike <see cref="Hidden"/>, which hides from all surfaces, this suppresses only MCP tool
	/// input schemas and prompt arguments — and, because the advertised schema and the accepted
	/// argument list share one option list, an MCP <c>tools/call</c> that supplies the option is
	/// rejected too. The option stays visible in help, in interactive and shell completion, and in
	/// exported documentation, and binds normally from the command line and the REPL. Mirrors
	/// <see cref="CommandBuilder.AutomationHidden(bool)"/> at the option level. This is a discovery
	/// filter, <b>not</b> an access-control boundary.
	/// </remarks>
	/// <param name="isAutomationHidden">Whether the option is hidden from programmatic surfaces.</param>
	/// <returns>The same builder instance.</returns>
	public OptionBuilder AutomationHidden(bool isAutomationHidden = true)
	{
		_setAutomationHidden(isAutomationHidden);

		return this;
	}
}
