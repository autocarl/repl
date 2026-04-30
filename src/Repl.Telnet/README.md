# Repl.Telnet

`Repl.Telnet` runs a Repl Toolkit session over a **Telnet-framed** transport.

It performs negotiation (including NAWS window size and TERMINAL-TYPE) and wires the result into the session metadata model.

## Install

```bash
dotnet add package Repl.Telnet
```

## Run a session (example)

```csharp
using System.Net.Sockets;
using Repl;
using Repl.Telnet;

var app = ReplApp.Create().UseDefaultInteractive();
app.Map("hello", () => "world");

using var client = new TcpClient("127.0.0.1", 23);
await using var stream = client.GetStream();
return await ReplTelnetSession.RunAsync(app, stream);
```

## Docs

- [Cookbook: Hosting Remote Sessions](https://repl.yllibed.org/cookbook/hosting-remote/) — WebSocket/Telnet setup, DI scopes, auth
- [Terminal Integration](https://repl.yllibed.org/reference/terminal-integration/) — NAWS window size, terminal type negotiation
