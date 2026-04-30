# Repl (meta-package)

**Repl Toolkit** is a **foundational building block** for .NET applications that need a serious command surface.

---

`Repl` is the recommended starting point for **Repl Toolkit**. It brings the default dependencies:

- `Repl.Core`
- `Repl.Defaults`
- `Repl.Protocol`

## Minimal app

```csharp
using Repl;

var app = ReplApp.Create().UseDefaultInteractive();
app.Map("hello", () => "world");

return app.Run(args);
```

## Docs

Full documentation at **[repl.yllibed.org](https://repl.yllibed.org/)**:

- [Installation & first app](https://repl.yllibed.org/getting-started/installation/)
- [Cookbook](https://repl.yllibed.org/cookbook/core-basics/) — guided examples from basics to MCP
- [Reference](https://repl.yllibed.org/reference/routes-and-parameters/) — routes, DI, modules, interactivity, MCP, and more
- [API reference](https://repl.yllibed.org/api/index.html)
