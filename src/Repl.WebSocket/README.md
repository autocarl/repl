# Repl.WebSocket

**Website:** [repl.yllibed.org](https://repl.yllibed.org/)

`Repl.WebSocket` runs a Repl Toolkit session over a **raw WebSocket** connection.

## Install

```bash
dotnet add package Repl.WebSocket
```

## Run a session (example)

```csharp
using System.Net.WebSockets;
using Repl;
using Repl.WebSocket;

var app = ReplApp.Create().UseDefaultInteractive();
app.Map("hello", () => "world");

WebSocket socket = /* connected socket */;
return await ReplWebSocketSession.RunAsync(app, socket);
```

## Notes

- Supports in-band terminal control messages (`@@repl:*`) for terminal/session metadata.

## Docs

- [Cookbook: Hosting Remote Sessions](https://repl.yllibed.org/cookbook/hosting-remote/) — WebSocket/Telnet setup, DI scopes, auth
- [Terminal Integration](https://repl.yllibed.org/reference/terminal-integration/) — capability detection, window size, ANSI
