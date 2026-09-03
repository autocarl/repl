# Execution Pipeline

This document describes the command execution pipeline in the Repl Toolkit,
from raw input to final output. The pipeline is driven by `CoreReplApp.ExecuteCoreAsync`.

```text
Input
  |
  v
+---------------------------+
| 1. Global Option Parsing  |
+---------------------------+
  |
  v
+---------------------------+
| 2. Prefix Resolution      |
+---------------------------+
  |
  v
+---------------------------+     +--- Shell completion
| 3. Pre-Execution Handling |---->+--- Help rendering
+---------------------------+     +--- Empty invocation (enter interactive)
  |  (short-circuits if matched)  +--- Ambient commands
  |
  v
+---------------------------+
| 4. Route Resolution       |
+---------------------------+
  |
  v
+---------------------------+
| 5. Banner Rendering       |
+---------------------------+
  |
  v
+---------------------------+
| 6. Command Option Parsing |
+---------------------------+
  |
  v
+---------------------------+
| 7. Argument Binding       |
+---------------------------+
  |
  v
+---------------------------+
| 8. Context Validation     |
+---------------------------+
  |
  v
+---------------------------+
| 9. Middleware + Handler    |
+---------------------------+
  |
  v
+---------------------------+
| 10. Result Processing     |
+---------------------------+
  |
  v
+---------------------------+
| 11. Output Transformation |
+---------------------------+
  |
  v
+---------------------------+
| 12. Exit Code             |
+---------------------------+
```

## Pipeline Stages

### 1. Global Option Parsing

`GlobalOptionParser.Parse()` extracts global options from the raw arguments before
any routing takes place. Recognized options:

- `--help` — triggers help rendering
- `--interactive` — enters interactive session mode
- `--no-logo` — suppresses the banner
- `--output:format` — selects the output transformer
- `--answer:key=value` — supplies pre-answered prompt values
- Custom global options registered through configuration

These tokens are consumed and removed from the argument list before the next stage.
Parsed custom global option values are stored in `IGlobalOptionsAccessor` (registered in DI),
making them available to middleware, DI service factories, and handlers in subsequent stages.

In interactive mode, CLI-level global options become session defaults — they persist across
all commands unless explicitly overridden per-command. Overrides are temporary and revert
to the session baseline on the next command.

### 2. Prefix Resolution

`ResolveUniquePrefixes()` expands abbreviated command names to their full registered
forms. If a prefix matches multiple commands, it is flagged as ambiguous and an error
is reported with the list of candidates.

### 3. Pre-Execution Handling

`TryHandlePreExecutionAsync()` checks for conditions that bypass normal command
dispatch:

- **Shell completion requests** — returns completion candidates for the current token.
- **Help rendering** — displays help for the resolved command or context.
- **Empty invocation** — no arguments provided; enters interactive mode.
- **Ambient commands** — built-in commands (e.g., `exit`, `clear`) handled directly.

If any of these conditions match, execution short-circuits and the remaining stages
are skipped.

### 4. Route Resolution

`RouteResolver.ResolveWithDiagnostics()` matches the remaining tokens against all
registered route templates. The resolver scores matches by segment type: literal
segments score higher than dynamic (parameterized) ones. The best-scoring match is
selected along with extracted route values.

On failure, the resolver produces diagnostics including similar command suggestions
computed via Levenshtein distance. See [commands.md](commands.md) for how commands
and routes are defined.

### 5. Banner Rendering

`TryRenderBannerAsync()` renders the application banner configured via `WithBanner()`.
The banner is suppressed when:

- `--no-logo` was passed
- The invocation is a protocol passthrough

### 6. Command Option Parsing

`InvocationOptionParser.Parse()` processes command-specific options from the remaining
tokens. Supported syntax:

```csharp
// All equivalent for valued options
--name value
--name=value
--name:value

// Boolean flags
--verbose       // true
--no-verbose    // false (negation prefix)

// End-of-options separator
-- remaining args are positional

// Response files
@responsefile.txt
```

See [configuration-reference.md](configuration-reference.md) for options configuration.

### 7. Argument Binding

`HandlerArgumentBinder.Bind()` resolves each handler parameter using the following
precedence:

1. `CancellationToken` — injected from the execution context
2. Explicit attributes — `[FromServices]`, `[FromContext]`
3. Options groups — `[ReplOptionsGroup]` for multi-property binding
4. Route values — values extracted during route resolution
5. Named options — values from command option parsing
6. Context/services — resolved from the DI container or execution context
7. Positional arguments — remaining unmatched tokens, in order
8. Default values — parameter defaults from the method signature
9. Null — for nullable types without other source

See [parameter-system.md](parameter-system.md) for full parameter binding details.

### 8. Context Validation

`ValidateContextsForMatchAsync()` runs optional validation delegates registered on
each context in the matched route's path hierarchy. Validation failures prevent
handler invocation and produce an error result.

### 9. Middleware and Handler Invocation

`ExecuteWithMiddlewareAsync()` builds the middleware chain from delegates registered
via `app.Use()`, then invokes the pipeline. The final stage calls
`CommandInvoker.InvokeAsync()` which executes the handler delegate.

The invoker supports synchronous, `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` handlers:

```csharp
// Synchronous, returns data
int Count() => items.Length;

// Async, returns data
async Task<Contact[]> ListAsync() => await store.ListAsync();
async ValueTask<string> DescribeAsync() => "ready";

// Void (implicit success)
void Run() { }
async Task RunAsync() { }
```

> **A handler's return value is always rendered as data — including `int`.** It never becomes the
> process exit code: `int Count() => 3` prints `3` and exits `0`. To set an explicit exit code,
> return `Results.Exit(code)` (or any `IExitResult`).

Once the handler has run, its return value is exposed as `ReplExecutionContext.Result`, so a
middleware registered with `app.Use(...)` can inspect or replace it after awaiting `next()`. A
middleware that short-circuits (never calls `next()`) may set `context.Result` to supply the result
that is rendered and classified in the handler's place.

### 10. Result Processing

The raw handler return value is unwrapped and interpreted:

- `IExitResult` — carries an explicit exit code and optional message.
- `ITuple` — destructured into result components.
- `EnterInteractiveResult` — transitions the session into interactive mode.
- Navigation transformations are applied if the result triggers context changes.
- Registered observers are notified of the result.

### 11. Output Transformation

`RenderOutputAsync()` selects an output format using this precedence:

1. Explicit format flag on the command
2. `--output:format` global option
3. `ReplOptions.Output.DefaultFormat`
4. `"human"` (fallback)

The matching `IOutputTransformer` formats the result and writes it to stdout.

### 12. Exit Code

Every run that reaches the core pipeline ends in exactly one `ReplExecutionOutcomeKind`, decided
once after every pipeline layer has run. (The one path outside the pipeline is a hosted-service
start/stop failure in `ReplApp.RunAsync` with `HostedServiceLifecycle` enabled, which still returns
`1` directly.) The kind is mapped to an integer by `ReplOptions.ExitCodes`
(`ExitCodeOptions`), then handed to the optional `ExitCodes.Resolver` hook whose return value is
final:

| Kind | Produced by | Default code |
|---|---|---|
| `Success` | success-like handler result (`text`/`success`, plain data, `void`), ambient commands (`exit`, `..`) that did their job, clean interactive exit | `0` |
| `Help` | `--help`, bare invocation that prints help, scoped-context help | `0` |
| `UsageError` | unknown command, ambiguous prefix, invalid global or command option, context validation failure, unknown `--output` format | `2` |
| `BindingError` | a handler argument could not be bound: token conversion failed or was missing, or a binder-resolved value (context value, `[FromServices]` dependency, typed global options service) was unavailable | `2` |
| `HandlerError` | handler returned `Results.Error` / `Validation` / `NotFound` / `Cancelled` | `1` |
| `HandlerExitCode` | handler returned an `IExitResult` — its code is used verbatim, the table is bypassed | `IExitResult.ExitCode` |
| `HandlerException` | the handler or a middleware threw | `1` |
| `Cancelled` | the run ended with an `OperationCanceledException` — the caller's `CancellationToken`, a cancelled prompt, or a handler that threw it | unmapped: the exception propagates; set `ExitCodes.Cancelled` (e.g. `130`) to return a code instead |
| `Interrupted` | reserved for process-signal bridges (SIGINT/SIGTERM); the core pipeline never produces it | — |
| `FrameworkError` | incompatible programmatic adapter, unsupported hosting capability | `1` |

```csharp
app.Options(options =>
{
    options.ExitCodes.UsageError = 64;          // EX_USAGE for scripts that follow sysexits
    options.ExitCodes.Help = 3;                 // a bare invocation must not look like success in CI
    options.ExitCodes.Cancelled = 130;          // return 128+SIGINT instead of throwing
    options.ExitCodes.Resolver = outcome =>     // final say, sees the structured outcome
        outcome.Kind == ReplExecutionOutcomeKind.HandlerException ? 70 : outcome.ExitCode;
});
```

The resolver receives a `ReplExecutionOutcome` (`Kind`, table-mapped `ExitCode`, the final `Result`
object when one exists, and the `Exception` that ended the run when applicable). It runs once per
one-shot run and is not invoked when `Kind` is `Cancelled` and `ExitCodes.Cancelled` is unset — the
exception is rethrown instead. In an interactive session it is invoked once per committed command
(to compute the shell-integration mark code, where a Ctrl+C cancellation carries the conventional
`130` unless `ExitCodes.Cancelled` overrides it) and once more when the session exits.

Nested sub-invocations (MCP tool calls executed through the Repl pipeline) always use the built-in
defaults and skip the resolver: the policy describes the *process* exit, and nested callers only test
for non-zero. The interactive loop applies the same table and resolver when it reports a command's
exit code in shell-integration marks, so the terminal decoration and the CLI agree.

## Error Handling

Errors at each stage produce targeted diagnostics:

- **Route resolution failure** — suggests similar commands using Levenshtein distance
  and shows context-level help.
- **Option parsing errors** — suggests the correct option name when a close match
  exists.
- **Binding errors** — renders a message identifying the missing or invalid parameter.
- **Handler exceptions** — caught and unwrapped from `TargetInvocationException`,
  then rendered as an error to stderr.
- **Cancellation** — in one-shot mode `OperationCanceledException` propagates to the caller
  unless `ReplOptions.ExitCodes.Cancelled` is set, in which case the run returns that code with
  `ReplExecutionOutcomeKind.Cancelled`; the interactive loop renders a cancellation message instead.

## Interactive Session Loop

When the application enters interactive mode (empty invocation or `--interactive`),
execution follows a loop:

```text
Prompt --> Input Tokenization --> History
  ^                                  |
  |                                  v
  |                          Ambient Commands
  |                                  |
  |                                  v
  |                      Prefix + Route Resolution
  |                                  |
  |                                  v
  |                          Command Dispatch
  |                                  |
  |                                  v
  |                              Output
  |                                  |
  +----------------------------------+
```

Each iteration runs through the same pipeline stages (4 through 12) described above,
preceded by ambient command checks. The session persists until an explicit `exit`
command or cancellation signal.

## See Also

- [commands.md](commands.md) — Command and route definition
- [parameter-system.md](parameter-system.md) — Parameter binding and attributes
- [configuration-reference.md](configuration-reference.md) — Options and app configuration
