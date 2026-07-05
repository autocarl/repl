# Terminal Shell Integration

Modern terminals understand semantic marks that delimit the prompt, the user input, and the command output. When those marks are present, the terminal can offer command navigation (jump between commands), command-aware selection and copy, success/failure decorations in the gutter, and sticky command headers.

Repl owns the prompt and the command lifecycle in interactive mode, so it can emit those marks itself — no shell script hooks required. The feature is opt-in:

```csharp
var app = ReplApp.Create()
    .UseTerminalIntegration();          // ShellIntegration = Auto by default

// or explicitly:
app.UseTerminalIntegration(options =>
{
    options.ShellIntegration = ShellIntegrationMode.Always;
});
```

Raw escape sequences are never exposed to command handlers; Repl chooses the protocol and emits the marks around its own prompt loop.

## What gets emitted

In interactive REPL mode, each prompt cycle is delimited with the FinalTerm semantic sequence (OSC 133), or the VS Code shell-integration sequence (OSC 633) when the VS Code integrated terminal is detected:

| Moment | Mark |
|---|---|
| Before the prompt text | `A` (prompt start) |
| After the prompt text, before input | `B` (input start) |
| After a committed line (VS Code only) | `E;<command line>` (command-line report) |
| Right before command execution | `C` (output start) |
| After the command completes | `D;<exit code>` (command end) |

Exit codes follow shell conventions: `0` for success, `1` for errors (failed results, unknown commands, validation failures), and `130` (128+SIGINT) when a command is cancelled with Ctrl+C. An abandoned cycle — Escape at the prompt, an empty line, or end of input — reports `D` without an exit code, the FinalTerm "command aborted" form.

The VS Code `E` mark reports the exact committed command line (with protocol escaping), which makes VS Code's command detection independent of what is visible on screen.

CLI one-shot mode emits no marks: Repl does not own the surrounding shell prompt there, and fake prompt markers would corrupt the host shell's own command navigation. Nested interaction prompts (`IReplInteractionChannel` questions asked *during* a command) emit no marks either — they are not shell prompts.

## Modes

`ShellIntegrationMode` mirrors the existing `AdvancedProgressMode` semantics:

- `Auto` (default) — emit when the terminal is known to render marks: the hosted session advertises `TerminalCapabilities.ShellIntegrationMarks`, or the local environment identifies Windows Terminal (`WT_SESSION`), VS Code (`TERM_PROGRAM=vscode`), or WezTerm (`TERM_PROGRAM=WezTerm`). Multiplexers (tmux, GNU screen) stay off: mark positioning is unreliable through panes.
- `Always` — emit whenever the structural gates allow it (see below). Useful for terminals that render marks but are not auto-detected, such as iTerm2 reached over SSH.
- `Never` — never emit.

Regardless of mode, marks are never written when:

- output is redirected and no hosted session is active;
- ANSI output is disabled (`NO_COLOR`, `TERM=dumb`, explicit `AnsiMode.Never`, ...);
- a command is streaming raw protocol bytes (protocol passthrough, including MCP stdio).

## Backend selection

The generic backend is OSC 133, understood by Windows Terminal, WezTerm, iTerm2, Ghostty, and others. When the VS Code integrated terminal is detected — `TERM_PROGRAM=vscode` locally, or a hosted session reporting a `vscode` terminal identity — Repl switches to the OSC 633 dialect and additionally reports the command line with `E`.

ConEmu is deliberately excluded from `Auto`: it renders OSC 9;4 progress but not FinalTerm marks.

## Hosted sessions

Hosted sessions (WebSocket, Telnet) receive marks when their reported terminal identity infers `TerminalCapabilities.ShellIntegrationMarks` (for example `Windows Terminal`, `wezterm`, `vscode`) or when the host sets the flag explicitly through `TerminalSessionOverrides`. See [Terminal Metadata](terminal-metadata.md).

## See Also

- [Interactive Loop](interactive-loop.md) — where the marks sit in the prompt cycle
- [Progress](progress.md#advanced-terminal-progress) — the OSC 9;4 progress integration
- [Terminal Metadata](terminal-metadata.md) — capability flags and how sessions advertise them
- [Configuration Reference](configuration-reference.md) — all options
