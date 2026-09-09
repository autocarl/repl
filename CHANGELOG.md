# Changelog

Notable consumer-facing changes to the Repl packages. Versions are assigned automatically by
Nerdbank.GitVersioning at pack time; this file groups changes by theme instead of by release.

## Unreleased

### Added — execution outcomes and exit-code policy

- Every run now ends in a structured `ReplExecutionOutcome` whose `ReplExecutionOutcomeKind`
  distinguishes `Success`, `Help`, `UsageError`, `BindingError`, `HandlerError`, `HandlerExitCode`,
  `HandlerException`, `Cancelled`, `Interrupted`, and `FrameworkError`. The kind is mapped to an
  integer by the new `ReplOptions.ExitCodes` (`ExitCodeOptions`) table, then passed to the optional
  `ExitCodes.Resolver` hook whose return value is the final exit code. An explicit `Results.Exit(n)`
  keeps its code verbatim (`HandlerExitCode`) but is still visible to the resolver. See
  `docs/execution-pipeline.md` (stage 12) and `docs/configuration-reference.md`.
- `ExitCodes.Cancelled` (`int?`) turns a cancellation through the caller's own token into an exit
  code instead of letting `OperationCanceledException` escape `RunAsync`. It is unset by default,
  which preserves the existing throwing behaviour; setting a `Resolver` also opts in to observing
  cancellation.
- `ExitCodes.Interrupted` (`int?`) maps a process signal bridged into a cooperative shutdown. Unset,
  the conventional `128 + signal` code the bridge carries is used.
- `ReplExecutionOutcome.Scope` (`ReplExitCodeScope`) tells a resolver whether it is computing the
  process exit code (`Process`, once per run) or one interactive command's shell-integration
  command-end mark (`ShellIntegrationMark`, only when a mark actually carries a code — so never with
  shell integration off, for a protocol-passthrough command, or for an abandoned prompt cycle).
- A resolver that throws no longer escapes the run: the table-mapped code is used and one diagnostic
  line is written to the session's error stream. Interactive sessions survive a faulty resolver, and
  a resolver failure on a failed command never replaces the original exception.
- `ReplExecutionContext.Result` exposes the handler's return value to middleware registered with
  `app.Use(...)`: readable and replaceable after `await next()`, settable by a short-circuiting
  middleware. `ReplNext` and the `Use` signature are unchanged.

### Changed — breaking: framework exit codes

These land together in the commit closing issue #81; a consumer bisecting an exit-code change can
anchor on that. (Package versions come from Nerdbank.GitVersioning at pack time, so this file names
none.)

- Framework refusals now exit `2` instead of `1`: unknown command, ambiguous prefix, invalid global
  or command option, option collision, context validation failure, unknown `--output` format,
  ambient-command misuse in one-shot mode (`exit` while disabled, `..`, `complete` without
  `--target`), help that cannot be rendered (`UsageError`), and arguments that cannot be bound,
  converted, or resolved from context/services (`BindingError`). Handler
  failures (`Results.Error`/`Validation`/`NotFound`, exceptions) still exit `1`, help and success
  still exit `0`. Set `ExitCodes.UsageError`/`BindingError` back to `1` to restore the old numbers.
  The interactive loop reports the same resolved codes in shell-integration `D;<code>` marks,
  including the mark for a command whose dispatch threw, which previously always reported `1`.
- Every `Run`/`RunAsync` overload now checks the caller's `CancellationToken` before doing any work —
  the hosted-service overloads before starting hosted services: a token that is already cancelled
  throws `OperationCanceledException` (or returns `ExitCodes.Cancelled` when mapped). Previously only
  `CoreReplApp.RunAsync` performed that check.
- A handler that raises `OperationCanceledException` without the caller having asked for cancellation
  is now a `HandlerException`: the message is rendered and the run exits `1`, where it previously
  either propagated silently or, with `ExitCodes.Cancelled` mapped, returned the cancellation code
  with no diagnostic at all. Only the caller's own token yields `Cancelled`. The interactive loop's
  Ctrl+C semantics are unchanged.
- Interactive `help` / `?` is now classified `Help` rather than a generic success, so an application
  that maps `ExitCodes.Help` separately sees its own code in the command-end mark. An ambient command
  that *failed* is still a `UsageError`, whatever it would have reported on success.
- Hosted-service start and stop failures in `ReplApp.RunAsync` now go through the exit-code policy as
  `FrameworkError` instead of returning a hard-coded `1`. The code is resolved once, after the whole
  lifecycle, so a failed shutdown outranks the command's own outcome and a resolver is handed exactly
  one outcome per run. An already-cancelled caller token also follows `ExitCodes.Cancelled` on that
  overload, without starting hosted services.
- A binding failure now carries the rendered refusal in `ReplExecutionOutcome.Result` alongside the
  `Exception`, matching the documented contract; previously only routing refusals did.
- A hosted-service failure carries its exception in the outcome, and a startup stopped by the
  caller's own token is a `Cancelled` outcome rather than a `FrameworkError`: it prints no startup
  error and, with no cancellation policy configured, propagates the `OperationCanceledException` like
  every other path. A shutdown that fails still outranks everything the run produced, including a
  cancellation the pipeline was propagating.
- An unknown `--output` format is a `UsageError` on every path, including while a failure was being
  reported and for an `EnterInteractive` payload — the interactive loop is then not entered. A
  diagnostic the caller never saw cannot stand as the run's outcome.
- `ReplApp.RunAsync(args, IReplHost, IServiceProvider, …)` observes an already-cancelled token before
  opening the session, so the caller's service factories are not resolved for a run nobody awaits.

### Compatibility notes — exit codes

- MCP tool calls (nested sub-invocations) always use the built-in exit-code defaults and ignore
  `ExitCodes.Resolver`; they only test for non-zero, so `IsError` is unaffected by the policy. The
  agent-visible failure text now reads "exit code 2" for usage and binding refusals.
- `Repl.Testing`'s per-command timeout still surfaces as `TimeoutException` when the app under test
  maps `ExitCodes.Cancelled`: the handle observes the run's own outcome instead of relying on the
  exception escaping. `RunCommandAsync` documents that exception.
- A handler-thrown `InvalidOperationException` is still rendered as a validation message, but it
  is classified `HandlerException` (not `BindingError`); only exceptions raised while binding
  arguments are `BindingError`.
- A handler that returns a bare `int` (or any scalar) is unchanged: the value is rendered as data
  and the run is a `Success`. The documentation previously implied otherwise; `Results.Exit(n)`
  remains the only return-value route to an explicit exit code.
- `Repl.Testing`'s `CommandExecution.ExitCode` follows the configured policy, so application test
  suites asserting `1` for unknown commands or invalid options need to expect `2` (or configure
  `ExitCodes`).
- An `IReplResult` whose `Kind` is not `text` or `success` is a `HandlerError` (exit `1`), including
  a kind the framework does not recognize. An unclassifiable result never reports success to a
  pipeline; use `Results.Exit(n)` to choose a code deliberately.
- `ReplExecutionOutcomeKind.Interrupted` is never produced by the core pipeline; it exists so a
  process-signal bridge can route SIGINT/SIGTERM outcomes through the same table and resolver.
- Exit codes are not range-checked. Keep them within `0`-`255`: POSIX `wait` exposes only the low
  eight bits to the parent process.

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
