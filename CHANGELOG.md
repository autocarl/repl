# Changelog

Notable consumer-facing changes to the Repl packages. Versions are assigned automatically by
Nerdbank.GitVersioning at pack time; this file groups changes by theme instead of by release.

## Unreleased

### Added — option visibility

- `.Hidden(bool isHidden = true)` on the option builder (`WithOption(name, option => option.Hidden())`)
  hides an option's canonical token, aliases, description, default, and value candidates from help,
  generated documentation, interactive/shell completion, and MCP tool schemas. The option remains a
  fully parsable, invocable part of the command line — hiding is a discovery filter, not access
  control. Available for direct command-handler parameters, options-group properties, manually
  registered global options (`ParsingOptions.GlobalOption(name).Hidden()`), and typed global options.
- `.HiddenAlias(alias, isHidden = true)` and `[ReplOption(HiddenAliases = [...])]` mark specific
  legacy/deprecated token spellings as parser-only: the canonical token and any current aliases stay
  discoverable, while the hidden alias keeps binding from the CLI/REPL for backward compatibility.
- `doc export` (and `docs <command path>`) reports `isHidden` / `isAutomationHidden` per option so an
  app author can inventory what a given command hides. Aggregate documentation (no target path) and
  MCP's `tools/list` always omit hidden options entirely — see `docs/commands.md` for the full
  visibility matrix.
- A hidden option must remain omittable for every provider that can build a discovery surface.
  Hiding a required options-group property fails immediately at `Map` time. Hiding a required direct
  handler parameter defers that check to the first time discovery runs against a real service
  provider (aggregate documentation build or MCP startup), since a DI/synthesized-progress fallback
  is only knowable once one exists — see the "Provider-aware requiredness" section of
  `docs/commands.md`.

### Changed — breaking

- `WithOption(name, configure)` is now the only fluent entry point for configuring an existing
  option's metadata (visibility included). This lands within the same change that introduces it —
  no previously published `Option(...)` API is removed by this release.

### Compatibility notes

- `doc export --json` (and other structured documentation exports) now unconditionally include the
  `isHidden` and `isAutomationHidden` fields on every option. A consumer validating that output
  against a closed schema (`additionalProperties: false`) will need to allow these two additive
  fields.
- The historical six-parameter `ParsingOptions.AddGlobalOptionCore` descriptor is preserved as a
  distinct overload (not folded into a defaulted parameter) so an already-compiled `Repl.Defaults`
  binary continues to work against a newer `Repl.Core`. The reverse is not guaranteed: this release's
  `Repl.Defaults` calls APIs that only exist in this release's `Repl.Core`, so upgrading only one of
  the two packages independently is not supported — upgrade them together.
