# Repl.Defaults

`Repl.Defaults` layers DI and “batteries included” composition on top of `Repl.Core`.

It provides:

- `ReplApp` facade
- default composition profiles (e.g. `UseDefaultInteractive`)
- hosted-session primitives (`StreamedReplHost`) used by transport integrations

## Install

```bash
dotnet add package Repl.Defaults
```

## Minimal app

```csharp
using Repl;

var app = ReplApp.Create().UseDefaultInteractive();
app.Map("hello", () => "world");

return app.Run(args);
```

## Docs

- [REPL Mode](https://repl.yllibed.org/getting-started/repl-mode/) — interactive session, scopes, history
- [Configuration](https://repl.yllibed.org/reference/configuration/) — `ReplRunOptions`, profiles
- [Architecture](https://repl.yllibed.org/reference/architecture/) — how the layers fit together
- [Dependency Injection](https://repl.yllibed.org/reference/dependency-injection/) — DI patterns for handlers
