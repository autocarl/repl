namespace Repl;

/// <summary>
/// Configures discovery metadata for a registered global option.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="OptionBuilder"/> rather than sharing it. Global options are
/// consumed before routing and never enter the documentation model, so they cannot reach an MCP tool
/// schema — an automation-visibility knob here could never do anything. Splitting the type makes that
/// unrepresentable instead of offering a method that silently does nothing.
/// </remarks>
public sealed class GlobalOptionBuilder
{
	private readonly ParsingOptions _owner;
	private readonly string _canonicalName;

	internal GlobalOptionBuilder(ParsingOptions owner, string canonicalName)
	{
		_owner = owner;
		_canonicalName = canonicalName;
	}

	/// <summary>
	/// Hides or shows one registered alias while leaving the global option's canonical token visible.
	/// Parsing and binding continue to accept the alias.
	/// </summary>
	/// <param name="alias">A registered alias token, with or without its long-option prefix.</param>
	/// <param name="isHidden">Whether the alias is hidden from discovery.</param>
	/// <returns>The same builder instance.</returns>
	public GlobalOptionBuilder HiddenAlias(string alias, bool isHidden = true)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(alias);
		_owner.SetGlobalOptionAliasHidden(_canonicalName, alias, isHidden);
		return this;
	}

	/// <summary>
	/// Hides or shows the global option on discovery surfaces without changing parsing or binding.
	/// </summary>
	/// <remarks>
	/// The option is omitted from help and from interactive and shell completion, but remains a valid
	/// parser input and binds normally when supplied explicitly. This is a discovery filter, not an
	/// access-control boundary: the token stays an invocable part of the command line.
	/// </remarks>
	/// <param name="isHidden">Whether the option is hidden.</param>
	/// <returns>The same builder instance.</returns>
	public GlobalOptionBuilder Hidden(bool isHidden = true)
	{
		_owner.SetGlobalOptionHidden(_canonicalName, isHidden);

		return this;
	}
}
