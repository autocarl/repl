using Repl.Documentation;

namespace Repl.Mcp;

/// <summary>
/// Removes automation-hidden options from a documentation model before anything MCP-facing reads it,
/// and withdraws any command that this would leave impossible to invoke.
/// </summary>
/// <remarks>
/// Applied once, at the model boundary, rather than at each MCP emitter. The generated tool schema
/// does not declare <c>additionalProperties: false</c>, so <see cref="McpToolAdapter"/>'s argument
/// allow-list is the only thing that turns an unadvertised option into a hard error. Should schema
/// generation and allow-listing ever read different option lists, an option would silently vanish
/// from the schema while remaining callable — projecting once makes that divergence unrepresentable.
/// <para>
/// Fully hidden options never arrive here: MCP always builds the aggregate documentation model, which
/// omits them already. Only the programmatic-only axis needs filtering.
/// </para>
/// </remarks>
internal static class McpAutomationProjection
{
	public static ReplDocumentationModel Apply(ReplDocumentationModel model)
	{
		if (!model.Commands.Any(HasUnavailableOption))
		{
			return model;
		}

		var projected = model.Commands
			.Select(static command => Apply(command))
			.OfType<ReplDocCommand>()
			.ToArray();

		// Resources are projected from the command list, so a withdrawn command must not linger there.
		var retained = projected
			.Select(static command => command.Path)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		return model with
		{
			Commands = projected,
			Resources = [.. model.Resources.Where(resource => retained.Contains(resource.Path))],
		};
	}

	internal static ReplDocCommand? Apply(ReplDocCommand command)
	{
		if (!HasUnavailableOption(command))
		{
			return command;
		}

		if (command.Options.Any(static option => IsUnavailable(option) && option.Required))
		{
			return null;
		}

		return command with
		{
			Options = [.. command.Options.Where(static option => !IsUnavailable(option))],
		};
	}

	// Withholding an option a caller cannot omit leaves no valid invocation at all: the client cannot
	// supply it and omitting it fails to bind, so every call would fail. Withdrawing the tool is
	// preferable to advertising an impossible one — and unlike the all-surfaces Hidden axis this is
	// not rejected at configuration time, because the human command line remains perfectly usable.
	private static bool HasUnavailableOption(ReplDocCommand command) =>
		command.Options.Any(static option => IsUnavailable(option));

	private static bool IsUnavailable(ReplDocOption option) =>
		option.IsAutomationHidden || option.Aliases.Count == 0;
}
