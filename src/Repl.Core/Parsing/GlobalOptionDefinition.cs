namespace Repl;

internal sealed record GlobalOptionDefinition(
	string Name,
	string CanonicalToken,
	IReadOnlyList<string> Aliases,
	string? DefaultValue,
	string? Description,
	Type ValueType,
	Type? OwnerType);
