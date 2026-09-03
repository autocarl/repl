# Changelog

Notable consumer-facing changes to the Repl packages. Versions are assigned automatically by
Nerdbank.GitVersioning at pack time; this file groups changes by theme instead of by release.

## Unreleased

### Added — execution outcomes and exit-code policy

- Every run that reaches the core pipeline now ends in a structured `ReplExecutionOutcome` whose
  `ReplExecutionOutcomeKind` distinguishes `Success`, `Help`, `UsageError`, `BindingError`,
  `HandlerError`, `HandlerExitCode`, `HandlerException`, `Cancelled`, `Interrupted` (reserved for
  process-signal bridges), and `FrameworkError`. The kind is mapped to an integer by the new
  `ReplOptions.ExitCodes` (`ExitCodeOptions`) table, then passed to the optional
  `ExitCodes.Resolver` hook whose return value is the final exit code. An explicit `Results.Exit(n)`
  keeps its code verbatim (`HandlerExitCode`) but is still visible to the resolver. See
  `docs/execution-pipeline.md` (stage 12) and `docs/configuration-reference.md`.
- `ExitCodes.Cancelled` (`int?`) turns a caller-token cancellation into an exit code instead of
  letting `OperationCanceledException` escape `RunAsync`. It is unset by default, which preserves
  the existing throwing behaviour.
- `ReplExecutionContext.Result` exposes the handler's return value to middleware registered with
  `app.Use(...)`: readable and replaceable after `await next()`, settable by a short-circuiting
  middleware. `ReplNext` and the `Use` signature are unchanged.

### Changed — breaking: framework exit codes

- Framework refusals now exit `2` instead of `1`: unknown command, ambiguous prefix, invalid global
  or command option, option collision, context validation failure, unknown `--output` format,
  ambient-command misuse in one-shot mode (`exit` while disabled, `..`, `complete` without
  `--target`), help that cannot be rendered (`UsageError`), and arguments that cannot be bound,
  converted, or resolved from context/services (`BindingError`). Handler
  failures (`Results.Error`/`Validation`/`NotFound`, exceptions) still exit `1`, help and success
  still exit `0`. Set `ExitCodes.UsageError`/`BindingError` back to `1` to restore the old numbers.
  The interactive loop reports the same resolved codes in shell-integration `D;<code>` marks.
- Every `Run`/`RunAsync` overload now checks the caller's `CancellationToken` before doing any work: a
  token that is already cancelled throws `OperationCanceledException` (or returns
  `ExitCodes.Cancelled` when mapped). Previously only `CoreReplApp.RunAsync` performed that check;
  the `ReplApp` overloads let a token-ignoring handler run to completion.

### Compatibility notes — exit codes

- MCP tool calls (nested sub-invocations) always use the built-in exit-code defaults and ignore
  `ExitCodes.Resolver`; they only test for non-zero, so `IsError` is unaffected by the policy. The
  agent-visible failure text now reads "exit code 2" for usage and binding refusals.
- Hosted-service start/stop failures in `ReplApp.RunAsync` (with `HostedServiceLifecycle` enabled)
  still return `1` directly and do not pass through `ExitCodes`; routing them through the policy is
  deferred until the pending process-signal work in the same file lands.
- `Repl.Testing`'s per-command timeout still surfaces as `TimeoutException` when the app under test
  maps `ExitCodes.Cancelled`: the handle checks its own timeout token after the run instead of
  relying on the exception escaping.
- A handler-thrown `InvalidOperationException` is still rendered as a validation message, but it
  is classified `HandlerException` (not `BindingError`); only exceptions raised while binding
  arguments are `BindingError`.
- A handler that returns a bare `int` (or any scalar) is unchanged: the value is rendered as data
  and the run is a `Success`. The documentation previously implied otherwise; `Results.Exit(n)`
  remains the only return-value route to an explicit exit code.
- `Repl.Testing`'s `CommandExecution.ExitCode` follows the configured policy, so application test
  suites asserting `1` for unknown commands or invalid options need to expect `2` (or configure
  `ExitCodes`).
- `ReplExecutionOutcomeKind.Interrupted` has no table entry and is never produced by the core
  pipeline; it is reserved for the process-signal bridge so SIGINT/SIGTERM outcomes can flow through
  the same resolver.

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
