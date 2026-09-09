# Configuration Reference

This is the complete configuration reference for the Repl Toolkit. All options are accessible through the `ReplOptions` object passed to `app.Options(...)`.

```csharp
var app = ReplApp.Create();
app.Options(o =>
{
    o.Interactive.Prompt = "$";
    o.Output.DefaultFormat = "json";
    o.Parsing.AllowResponseFiles = false;
});
```

See also: [Commands](commands.md) | [Shell Completion](shell-completion.md) | [Interaction](interaction.md) | [Progress](progress.md)

## ReplOptions

The root configuration object. Exposes the following section properties:

- `Parsing` — Command-line parsing behavior
- `Interactive` — REPL session and prompt settings
- `Output` — Formatting, theming, and rendering
- `Binding` — Parameter binding behavior
- `Capabilities` — Terminal capability declarations
- `AmbientCommands` — Built-in command toggles and custom ambient commands
- `Interaction` — Progress and prompt fallback settings
- `ShellCompletion` — Shell completion installation and behavior

## ParsingOptions

Accessed via `ReplOptions.Parsing`.

- `AllowUnknownOptions` (`bool`, default: `false`) — Allow options not explicitly registered.
- `OptionCaseSensitivity` (`ReplCaseSensitivity`, default: `CaseSensitive`) — Option name case sensitivity.
- `AllowResponseFiles` (`bool`, default: `true`) — Expand response files (`@args.rsp`).
- `NumericCulture` (`NumericParsingCulture`, default: `Invariant`) — Culture used for numeric conversions.

### Methods

- `AddRouteConstraint(name, predicate)` — Register a named route constraint.
- `AddGlobalOption<T>(name, aliases, defaultValue)` — Register a global option available to all commands.
- `AddGlobalOption(name, typeName, aliases, defaultValue)` — Register a global option using a type name string (`"int"`, `"bool"`, `"guid"`, etc.).

### IGlobalOptionsAccessor

Registered automatically in DI. Provides typed access to parsed global option values from middleware, DI factories, and handlers.

- `GetValue<T>(name, defaultValue)` — Get typed value, falling back to registration default then caller default.
- `GetRawValues(name)` — Get all raw string values (supports repeated options).
- `HasValue(name)` — Check if the option was explicitly provided.
- `GetOptionNames()` — Enumerate all option names with values.

Values are updated after each global option parsing pass (per-invocation in interactive mode).

### UseGlobalOptions&lt;T&gt;()

Extension method on `ReplApp`. Registers a typed class whose public settable properties become global options. The class is available via DI, populated from parsed values. Property names are converted to kebab-case (`MaxRetries` → `--max-retries`). See [Commands — Accessing global options](commands.md#accessing-global-options-outside-handlers).

## InteractiveOptions

Accessed via `ReplOptions.Interactive`.

- `Prompt` (`string`, default: `">"`) — REPL prompt text.
- `InteractivePolicy` (`InteractivePolicy`, default: `Auto`) — Controls interactive mode activation: `Auto`, `Always`, or `Never`.
- `HistoryProvider` (`IHistoryProvider?`, default: `null`) — Custom history provider.
- `Autocomplete` (`AutocompleteOptions`) — Nested autocomplete options (see below).

### AutocompleteOptions

Accessed via `ReplOptions.Interactive.Autocomplete`.

- `Mode` (`AutocompleteMode`, default: `Auto`) — Autocomplete activation mode.
- `Presentation` (`AutocompletePresentation`, default: `Hybrid`) — How suggestions are displayed.
- `MaxVisibleSuggestions` (`int`, default: `8`) — Maximum number of visible suggestions.
- `CaseSensitive` (`bool`, default: `false`) — Whether matching is case-sensitive.
- `EnableFuzzyMatching` (`bool`, default: `false`) — Enable fuzzy matching for suggestions.
- `LiveHintEnabled` (`bool`, default: `true`) — Show inline hint while typing.
- `LiveHintMaxAlternatives` (`int`, default: `5`) — Maximum alternatives shown in live hint.
- `ShowContextAlternatives` (`bool`, default: `true`) — Show context-aware alternatives.
- `ShowInvalidAlternatives` (`bool`, default: `true`) — Show invalid alternatives in suggestions.
- `ColorizeInputLine` (`bool`, default: `true`) — Colorize the input line.
- `ColorizeHintAndMenu` (`bool`, default: `true`) — Colorize hints and the suggestion menu.

## OutputOptions

Accessed via `ReplOptions.Output`.

- `DefaultFormat` (`string`, default: `"human"`) — Default output format.
- `AnsiMode` (`AnsiMode`, default: `Auto`) — ANSI color support mode.
- `ThemeMode` (`ThemeMode`, default: `Auto`) — Theme mode: `Auto`, `Light`, or `Dark`.
- `PaletteProvider` (`IAnsiPaletteProvider`, default: `DefaultAnsiPaletteProvider`) — Custom color palette provider.
- `BannerEnabled` (`bool`, default: `true`) — Enable banner output.
- `BannerFormats` (`ISet<string>`, default: `{"human"}`) — Output formats that display banners.
- `ColorizeStructuredInteractive` (`bool`, default: `true`) — Colorize JSON/XML in interactive mode.
- `PreferredWidth` (`int?`, default: `null`) — Preferred render width. `null` uses automatic detection.
- `FallbackWidth` (`int`, default: `120`) — Fallback width when the terminal is unavailable.
- `ResultFlow` (`ResultFlowOptions`) - Paging and large-result behavior.
- `JsonSerializerOptions` (`JsonSerializerOptions`, default: Web defaults + indented) — JSON serializer options.

Built-in transformers: `human`, `json`, `xml`, `yaml`, `markdown`.

### ResultFlowOptions

Accessed via `ReplOptions.Output.ResultFlow`.

- `DefaultPageSize` (`int`, default: `100`) - Page size used when no caller or terminal hint provides one.
- `MaxPageSize` (`int`, default: `1000`) - Maximum accepted page size.
- `ReservedVisibleRows` (`int`, default: `2`) - Rows reserved when computing terminal-visible data rows.
- `DefaultPagerMode` (`ReplPagerMode`, default: `Auto`) - Pager behavior for human formats.
- `PagerRenderers` (`IReadOnlyList<IReplPagerRenderer>`) - Custom interactive pager renderers keyed by pager mode.
- `MaxBufferedLines` (`int`, default: `10000`) - Maximum content lines buffered by interactive viewport pagers.
- `ProgrammaticMaxInlineBytes` (`int`, default: `65536`) - Reserved for programmatic inline payload policy.

Register custom pager renderers with `UsePagerRenderer(renderer)`. Use
`RemovePagerRenderer(mode)` or `ClearPagerRenderers()` to alter the configured
renderer set.

### OutputOptions Methods

- `AddTransformer(name, transformer)` — Register a custom output transformer.
- `AddAlias(alias, format)` — Register a format alias.

## BindingOptions

Accessed via `ReplOptions.Binding`.

- `AggregateConversionErrors` (`bool`, default: `true`) — Aggregate all conversion errors instead of failing on the first.

## CapabilityOptions

Accessed via `ReplOptions.Capabilities`.

- `SupportsAnsi` (`bool`, default: `true`) — Declare whether the terminal supports ANSI escape sequences.

## ExitCodeOptions

Accessed via `ReplOptions.ExitCodes`. Maps each `ReplExecutionOutcomeKind` to the process exit code
of a top-level run; nested MCP sub-invocations always use the defaults and skip the resolver.

- `Success` (`int`, default: `0`) — Success-like handler result or clean interactive exit.
- `Help` (`int`, default: `0`) — `--help`, a bare invocation that prints help, scoped-context help.
- `UsageError` (`int`, default: `2`) — Unknown command, ambiguous prefix, invalid option, context validation failure, unknown output format.
- `BindingError` (`int`, default: `2`) — A handler argument could not be bound: token conversion failed or was missing, or a binder-resolved value (context value, `[FromServices]` dependency, typed global options service) was unavailable.
- `HandlerError` (`int`, default: `1`) — Handler returned an error-like `IReplResult`.
- `HandlerException` (`int`, default: `1`) — Handler or middleware threw.
- `Cancelled` (`int?`, default: `null`) — Cancellation through the caller's own token, during the command or while hosted services were starting. `null` rethrows the `OperationCanceledException` unless a `Resolver` is set, in which case the resolver is handed `130` (`128 + SIGINT`); a value is returned instead. A handler that raises `OperationCanceledException` without the caller having asked for cancellation is a `HandlerException`, not a cancellation.
- `Interrupted` (`int?`, default: `null`) — A process signal (SIGINT, Ctrl+Break, SIGTERM) turned into a cooperative shutdown by a process-signal handler. `null` uses the conventional `128 + signal` code the handler supplies, falling back to `130` when it supplies none; set it to publish one code for every signal. The core pipeline never produces this kind.
- `FrameworkError` (`int`, default: `1`) — Incompatible programmatic adapter, unsupported hosting capability, or a hosted-service start/stop failure. The outcome carries the exception that caused it; when a shutdown failure suppressed an exception the run was propagating, that is an `AggregateException` of both. Neither a cancellation nor an interruption falls back to this code.
- `Resolver` (`Func<ReplExecutionOutcome, int>?`, default: `null`) — Final interception hook. Receives the outcome with its table-mapped `ExitCode`; its return value wins. Also sees `HandlerExitCode` outcomes (explicit `Results.Exit`), which bypass the table. `ReplExecutionOutcome.Scope` distinguishes the process exit code (`ReplExitCodeScope.Process`, once per run) from one interactive command's shell-integration mark (`ReplExitCodeScope.ShellIntegrationMark`, only when a mark actually carries a code). Setting a resolver also opts in to observing cancellation. It must not throw: an exception degrades to the table-mapped code plus one diagnostic line on the error stream.

Codes should stay within `0`-`255` — POSIX `wait` exposes only the low eight bits to the parent
process. Repl passes a configured code through unchanged rather than clamping it.

## AmbientCommandOptions

Accessed via `ReplOptions.AmbientCommands`.

- `ExitCommandEnabled` (`bool`, default: `true`) — Enable the built-in `exit` command.
- `ShowHistoryInHelp` (`bool`, default: `false`) — Show the `history` command in help output.
- `ShowCompleteInHelp` (`bool`, default: `false`) — Show the `complete` command in help output.

### AmbientCommandOptions Methods

- `MapAmbient(name, handler, description)` — Register a custom ambient command.

## InteractionOptions

Accessed via `ReplOptions.Interaction`.

These options are configured through `app.Options(...)`. Repl does not currently auto-bind them from `IConfiguration`.

- `DefaultProgressLabel` (`string`, default: `"Progress"`) — Default label for progress indicators.
- `ProgressTemplate` (`string`, default: `"{label}: {percent:0}%"`) — Progress display template. Supports placeholders: `{label}`, `{percent}`, `{percent:0}`, `{percent:0.0}`.
- `AdvancedProgressMode` (`AdvancedProgressMode`, default: `Auto`) — Controls whether compatible hosts emit advanced terminal progress sequences. See [Progress](progress.md#advanced-terminal-progress).
- `PromptFallback` (`PromptFallback`, default: `UseDefault`) — Behavior when interactive prompts are unavailable.

## TerminalIntegrationOptions

Configured through `app.UseTerminalIntegration(...)` (opt-in; no marks are emitted without the call). See [Terminal Shell Integration](terminal-shell-integration.md).

- `ShellIntegration` (`ShellIntegrationMode`, default: `Auto`) — Controls whether shell-integration lifecycle marks (OSC 133 / OSC 633) are emitted around the interactive prompt and command execution.

## ShellCompletionOptions

Accessed via `ReplOptions.ShellCompletion`. See [Shell Completion](shell-completion.md) for setup details.

- `Enabled` (`bool`, default: `true`) — Enable shell completion support.
- `SetupMode` (`ShellCompletionSetupMode`, default: `Manual`) — Completion setup mode.
- `PreferredShell` (`ShellKind?`, default: `null`) — Preferred shell for completion. `null` uses automatic detection.
- `PromptOnce` (`bool`, default: `true`) — Only prompt the user once for completion setup.
- `ProviderTimeout` (`TimeSpan`, default: 1 second) — Deadline applied to each opted-in `WithCompletion` provider on the completion bridge; a stalled provider is abandoned and completion degrades to static candidates.
- `StateFilePath` (`string?`, default: `null`) — Path to the completion state file.
- `BashProfilePath` (`string?`, default: `null`) — Custom path for the Bash profile.
- `PowerShellProfilePath` (`string?`, default: `null`) — Custom path for the PowerShell profile.
- `ZshProfilePath` (`string?`, default: `null`) — Custom path for the Zsh profile.
- `FishProfilePath` (`string?`, default: `null`) — Custom path for the Fish profile.
- `NuProfilePath` (`string?`, default: `null`) — Custom path for the Nushell profile.

## ReplRunOptions

A record passed to `app.RunAsync(...)` to control runtime behavior. Separate from `ReplOptions`.

- `HostedServiceLifecycle` (`HostedServiceLifecycleMode`, default: `None`) — Hosted service lifecycle mode.
- `AnsiSupport` (`AnsiMode`, default: `Auto`) — ANSI support mode for this run.
- `TerminalOverrides` (`TerminalSessionOverrides?`, default: `null`) — Terminal session overrides.
