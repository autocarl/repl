using Repl.Internal.Options;

namespace Repl;

internal sealed class RouteDefinition(
	RouteTemplate template,
	CommandBuilder command,
	int moduleId)
{
	public RouteTemplate Template { get; } = template;

	public CommandBuilder Command { get; } = command;

	public int ModuleId { get; } = moduleId;

	// Read through to the builder rather than snapshotting: a fluent visibility change after
	// Map publishes a new schema, and every already-cached routing graph holds these same
	// RouteDefinition instances — so the change is observed with no cache plumbing.
	public OptionSchema OptionSchema => Command.OptionSchema;
}
