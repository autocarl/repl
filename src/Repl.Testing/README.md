# Repl.Testing

**Website:** [repl.yllibed.org](https://repl.yllibed.org/)

`Repl.Testing` is an in-memory harness for **multi-step** and **multi-session** tests over a Repl command surface.

## Install

```bash
dotnet add package Repl.Testing
```

## Example

```csharp
using Repl;
using Repl.Testing;

await using var host = ReplTestHost.Create(() =>
{
    var app = ReplApp.Create().UseDefaultInteractive();
    app.Map("hello", () => "world");
    return app;
});

await using var session = await host.OpenSessionAsync();
var execution = await session.RunCommandAsync("hello --no-logo");
```

## Docs

- [Cookbook: Testing](https://repl.yllibed.org/cookbook/testing/) — test host setup, typed assertions, multi-session, interaction supply
- [Best Practices](https://repl.yllibed.org/reference/best-practices/) — test-first patterns and testing at the command level
