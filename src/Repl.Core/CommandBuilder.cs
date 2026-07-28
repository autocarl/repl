using System.Reflection;
using Repl.Internal.Options;

namespace Repl;

/// <summary>
/// Configures metadata and behavior for a mapped command.
/// </summary>
public sealed class CommandBuilder
{
	private readonly Dictionary<string, CompletionDelegate> _completions =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly Dictionary<string, CompletionProviderScope> _completionScopes =
		new(StringComparer.OrdinalIgnoreCase);

	// Option visibility lives on this immutable schema, not on the builder, so every discovery
	// surface reads one source. Swapped whole via CompareExchange rather than mutated: no lock,
	// because a blocking wait on another managed thread deadlocks on single-threaded WASM.
	private OptionSchema _optionSchema = OptionSchema.Empty;

	// Derived discovery state (most visibly the MCP tool snapshot) is rebuilt only when routing
	// is invalidated, so any visibility change made after Map has to say so.
	private readonly Action<bool>? _invalidateRouting;
	private readonly Func<ReplCaseSensitivity>? _resolveOptionCaseSensitivity;

	/// <summary>
	/// Initializes a new instance of the <see cref="CommandBuilder"/> class.
	/// </summary>
	/// <param name="route">The command route template.</param>
	/// <param name="handler">The command handler delegate.</param>
	/// <param name="invalidateRouting">
	/// Invoked when metadata that discovery surfaces derive from changes after registration.
	/// </param>
	/// <param name="resolveOptionCaseSensitivity">
	/// Resolves the current global option-token comparison mode for post-registration visibility changes.
	/// </param>
	internal CommandBuilder(
		string route,
		Delegate handler,
		Action<bool>? invalidateRouting = null,
		Func<ReplCaseSensitivity>? resolveOptionCaseSensitivity = null)
	{
		Route = route;
		Handler = handler;
		_invalidateRouting = invalidateRouting;
		_resolveOptionCaseSensitivity = resolveOptionCaseSensitivity;
		SupportsHostedProtocolPassthrough = ComputeSupportsHostedProtocolPassthrough(handler);
	}

	/// <summary>
	/// Gets the command route.
	/// </summary>
	public string Route { get; }

	/// <summary>
	/// Gets the command handler.
	/// </summary>
	public Delegate Handler { get; }

	/// <summary>
	/// Gets the command description.
	/// </summary>
	public string? Description { get; private set; }

	/// <summary>
	/// Gets the configured aliases.
	/// </summary>
	public IReadOnlyList<string> Aliases { get; private set; } = [];

	/// <summary>
	/// Gets a value indicating whether this command is hidden from discovery surfaces.
	/// </summary>
	public bool IsHidden { get; private set; }

	/// <summary>
	/// Gets parameter completion providers keyed by target name.
	/// </summary>
	public IReadOnlyDictionary<string, CompletionDelegate> Completions => _completions;

	/// <summary>
	/// Gets a value indicating whether this command reserves stdin/stdout for a protocol handler.
	/// </summary>
	public bool IsProtocolPassthrough { get; private set; }

	/// <summary>
	/// Gets a value indicating whether the handler can run protocol passthrough in hosted sessions.
	/// </summary>
	internal bool SupportsHostedProtocolPassthrough { get; }

	/// <summary>
	/// Gets the banner delegate rendered before command execution.
	/// </summary>
	public Delegate? Banner { get; private set; }

	/// <summary>
	/// Gets the rich markdown description body.
	/// Used for agent tool descriptions and documentation export.
	/// </summary>
	public string? Details { get; private set; }

	/// <summary>
	/// Gets the structured behavioral annotations for this command.
	/// </summary>
	public CommandAnnotations? Annotations { get; private set; }

	/// <summary>
	/// Gets a value indicating whether this command is a resource (data to consult).
	/// </summary>
	public bool IsResource { get; private set; }

	/// <summary>
	/// Gets a value indicating whether this command is a prompt source.
	/// </summary>
	public bool IsPrompt { get; private set; }

	/// <summary>
	/// Gets declared answer slots for interactive prompts.
	/// </summary>
	public IReadOnlyList<AnswerDeclaration> Answers => _answers;

	private readonly List<AnswerDeclaration> _answers = [];

	/// <summary>
	/// Gets generic metadata entries for extensibility.
	/// </summary>
	public IReadOnlyDictionary<string, object> Metadata => _metadata;

	private readonly Dictionary<string, object> _metadata = new(StringComparer.OrdinalIgnoreCase);

	/// <summary>
	/// Sets a command description.
	/// </summary>
	/// <param name="text">Description text.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithDescription(string text)
	{
		Description = string.IsNullOrWhiteSpace(text)
			? throw new ArgumentException("Description cannot be empty.", nameof(text))
			: text;
		return this;
	}

	/// <summary>
	/// Sets aliases for the command.
	/// </summary>
	/// <param name="aliases">Alias list.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithAlias(params string[] aliases)
	{
		ArgumentNullException.ThrowIfNull(aliases);

		var normalized = aliases
			.Where(alias => !string.IsNullOrWhiteSpace(alias))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		if (normalized.Length == 0)
		{
			throw new ArgumentException("At least one alias is required.", nameof(aliases));
		}

		Aliases = normalized;
		return this;
	}

	/// <summary>
	/// Configures one option's metadata.
	/// </summary>
	/// <remarks>
	/// The callback shape is deliberate: this type and <see cref="OptionBuilder"/> both expose
	/// <c>Hidden</c> and <c>AutomationHidden</c>, so an API returning the option builder would let
	/// <c>.Option("x").Hidden()</c> sit in a chain reading exactly like the command-level call while
	/// meaning something quite different, with nothing to signal that the subject changed. Passing a
	/// callback keeps the subject explicit and the chain on the command.
	/// </remarks>
	/// <param name="targetName">Handler parameter or options-group property name.</param>
	/// <param name="configure">Receives the option metadata builder.</param>
	/// <returns>The same command builder instance.</returns>
	/// <exception cref="KeyNotFoundException">No such option target is registered for this command.</exception>
	/// <exception cref="InvalidOperationException">
	/// <paramref name="configure"/> hid an options-group property that a required arity or
	/// non-omittable CLR shape makes impossible to omit from an invocation.
	/// </exception>
	public CommandBuilder WithOption(string targetName, Action<OptionBuilder> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		configure(SelectOption(targetName));

		return this;
	}

	private OptionBuilder SelectOption(string targetName)
	{
		targetName = string.IsNullOrWhiteSpace(targetName)
			? throw new ArgumentException("Option target name cannot be empty.", nameof(targetName))
			: targetName;
		var schema = OptionSchema;
		if (!schema.TryGetParameter(targetName, out var parameter)
			|| parameter.Mode == ReplParameterMode.ArgumentOnly)
		{
			// List the candidates: the target is the CLR parameter or property name, not the rendered
			// token, and that mismatch is the mistake this exception almost always reports.
			var candidates = schema.Parameters.Values
				.Where(static candidate => candidate.Mode != ReplParameterMode.ArgumentOnly)
				.Select(static candidate => candidate.Name)
				.Order(StringComparer.Ordinal);

			throw new KeyNotFoundException(
				$"No option target named '{targetName}' is registered for command '{Route}'. "
				+ $"Known option targets: {string.Join(", ", candidates)}.");
		}

		return new OptionBuilder(
			isHidden => UpdateOptionParameter(
				targetName,
				current => current with { IsHidden = isHidden },
				isVisibilityRetraction: isHidden),
			isAutomationHidden => UpdateOptionParameter(
				targetName,
				current => current with { IsAutomationHidden = isAutomationHidden },
				isVisibilityRetraction: isAutomationHidden),
			(alias, isHidden) => UpdateOptionAliasVisibility(targetName, alias, isHidden));
	}

	internal OptionSchema OptionSchema => Volatile.Read(ref _optionSchema);

	internal void AttachOptionSchema(OptionSchema schema) => Volatile.Write(ref _optionSchema, schema);

	/// <summary>
	/// Publishes a new schema with the target parameter's metadata updated.
	/// </summary>
	/// <remarks>
	/// A retry loop rather than a plain write: the schema is also the parsing contract, so a
	/// concurrent update must not drop the other one. Unknown targets are impossible here because
	/// <see cref="SelectOption"/> already rejected them.
	/// </remarks>
	private void UpdateOptionParameter(
		string targetName,
		Func<OptionSchemaParameter, OptionSchemaParameter> update,
		bool isVisibilityRetraction)
	{
		while (true)
		{
			var current = Volatile.Read(ref _optionSchema);
			if (!current.TryGetParameter(targetName, out var parameter))
			{
				return;
			}

			var updated = update(parameter);
			if (updated == parameter)
			{
				return;
			}

			var candidate = current.WithParameter(updated);

			// Validate before publishing so the throw carries the caller's own Hidden() frame and
			// the schema is never left in a state no invocation could satisfy.
			ValidateOptionVisibility(candidate);
			if (ReferenceEquals(Interlocked.CompareExchange(ref _optionSchema, candidate, current), current))
			{
				_invalidateRouting?.Invoke(isVisibilityRetraction);
				return;
			}
		}
	}

	private void UpdateOptionAliasVisibility(string targetName, string alias, bool isHidden)
	{
		while (true)
		{
			var current = Volatile.Read(ref _optionSchema);
			var candidate = current.WithAliasVisibility(
				targetName,
				alias,
				isHidden,
				_resolveOptionCaseSensitivity?.Invoke());
			if (ReferenceEquals(candidate, current))
			{
				// Already in the requested state: WithAliasVisibility returned the same instance.
				// Mirrors UpdateOptionParameter's no-op early exit — no schema swap, no routing
				// invalidation, over a call that changed nothing.
				return;
			}

			if (ReferenceEquals(Interlocked.CompareExchange(ref _optionSchema, candidate, current), current))
			{
				_invalidateRouting?.Invoke(isHidden);
				return;
			}
		}
	}

	internal void ValidateOptionVisibility() => ValidateOptionVisibility(OptionSchema);

	/// <summary>
	/// Rejects a hidden option that no invocation could omit.
	/// </summary>
	/// <remarks>
	/// Runs at configuration time only — from <see cref="CoreReplApp.Map(string, Delegate)"/> for the
	/// declarative form and from the fluent setter for the other. It deliberately does not run on any
	/// execution or completion path: <c>Run</c> reports failures as an exit code and has no general
	/// catch, so throwing from there escapes the host unhandled.
	/// </remarks>
	private void ValidateOptionVisibility(OptionSchema schema)
	{
		foreach (var parameter in schema.Parameters.Values)
		{
			if (parameter.Mode == ReplParameterMode.ArgumentOnly || !parameter.IsHidden)
			{
				continue;
			}

			// Two independent ways an option can be mandatory, and each needs the right evidence.
			//
			// Only an EXPLICITLY declared arity counts: an inferred ExactlyOne describes how many
			// values the token consumes when present, not whether the option may be absent, so a
			// nullable reference parameter infers ExactlyOne yet binds null happily when omitted.
			//
			// The CLR shape is the other authority. Direct handler parameters cannot be rejected at
			// mapping time, though: Run and MCP may supply an external provider later, and the binder
			// consults it before enforcing either lower bound. Provider-aware documentation validates
			// those paths at the discovery boundary. Options-group properties never reach that fallback,
			// so their impossible hidden configuration remains an immediate error.
			var requiresFallback = parameter.ExplicitArity is ReplArity.ExactlyOne or ReplArity.OneOrMore
				|| !parameter.CanBeOmitted;
			if (!requiresFallback || parameter.SupportsServiceFallback)
			{
				continue;
			}

			// Name the rendered token as well as the CLR target: an operator greps argv for
			// '--internal-token' and would not find the parameter name anywhere.
			throw new HiddenRequiredOptionException(
				parameter.Name,
				schema.ResolveDisplayToken(parameter.Name),
				Route);
		}
	}

	/// <summary>
	/// Adds a completion provider for a target parameter, invoked by in-process surfaces only
	/// (the interactive Tab menu and the <c>complete</c> ambient command).
	/// </summary>
	/// <param name="targetName">Route or option target name.</param>
	/// <param name="provider">Completion delegate.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithCompletion(string targetName, CompletionDelegate provider) =>
		WithCompletion(targetName, provider, CompletionProviderScope.Interactive);

	/// <summary>
	/// Adds a completion provider for a target parameter with an explicit surface scope.
	/// Use <see cref="CompletionProviderScope.InteractiveAndShell"/> to also serve the shell
	/// completion bridge — only for providers fast enough for a blocking shell Tab, since the
	/// bridge spawns a new process per completion request.
	/// </summary>
	/// <param name="targetName">Route or option target name.</param>
	/// <param name="provider">Completion delegate.</param>
	/// <param name="scope">Surfaces allowed to invoke the provider.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithCompletion(string targetName, CompletionDelegate provider, CompletionProviderScope scope)
	{
		targetName = string.IsNullOrWhiteSpace(targetName)
			? throw new ArgumentException("Target name cannot be empty.", nameof(targetName))
			: targetName;
		ArgumentNullException.ThrowIfNull(provider);

		_completions[targetName] = provider;
		_completionScopes[targetName] = scope;
		return this;
	}

	/// <summary>
	/// True when the target's completion provider opted into the shell completion bridge.
	/// </summary>
	internal bool IsCompletionShellScoped(string targetName) =>
		_completionScopes.TryGetValue(targetName, out var scope)
		&& scope == CompletionProviderScope.InteractiveAndShell;

	/// <summary>
	/// Registers a banner delegate displayed before command execution.
	/// Unlike <see cref="WithDescription"/>, which is structural metadata visible in help and documentation,
	/// banners are display-only messages that appear at runtime.
	/// </summary>
	/// <param name="bannerProvider">Banner delegate with injectable parameters.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithBanner(Delegate bannerProvider)
	{
		ArgumentNullException.ThrowIfNull(bannerProvider);
		Banner = bannerProvider;
		return this;
	}

	/// <summary>
	/// Registers a static banner string displayed before command execution.
	/// Unlike <see cref="WithDescription"/>, which is structural metadata visible in help and documentation,
	/// banners are display-only messages that appear at runtime.
	/// </summary>
	/// <param name="text">Banner text.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithBanner(string text)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(text);
		Banner = () => text;
		return this;
	}

	/// <summary>
	/// Marks a command as hidden or visible.
	/// </summary>
	/// <param name="isHidden">True to hide the command.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder Hidden(bool isHidden = true)
	{
		IsHidden = isHidden;
		_invalidateRouting?.Invoke(isHidden);
		return this;
	}

	/// <summary>
	/// Marks this command as protocol passthrough.
	/// In this mode, repl diagnostics are routed to stderr and interactive stdin reads are skipped.
	/// When handlers request <see cref="IReplIoContext"/>, <see cref="IReplIoContext.Output"/> remains the protocol stream
	/// (stdout in local CLI passthrough), while framework output stays on stderr.
	/// For hosted sessions, handlers should request <see cref="IReplIoContext"/> to access transport streams explicitly.
	/// </summary>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder AsProtocolPassthrough()
	{
		IsProtocolPassthrough = true;
		return this;
	}

	// ── Rich metadata ──────────────────────────────────────────────────

	/// <summary>
	/// Sets a rich markdown description body for agent tool descriptions
	/// and documentation export.
	/// </summary>
	/// <param name="markdown">Markdown content.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithDetails(string markdown)
	{
		Details = string.IsNullOrWhiteSpace(markdown)
			? throw new ArgumentException("Details cannot be empty.", nameof(markdown))
			: markdown;
		return this;
	}

	/// <summary>
	/// Adds a generic metadata entry.
	/// </summary>
	/// <param name="key">Metadata key.</param>
	/// <param name="value">Metadata value.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithMetadata(string key, object value)
	{
		key = string.IsNullOrWhiteSpace(key)
			? throw new ArgumentException("Metadata key cannot be empty.", nameof(key))
			: key;
		ArgumentNullException.ThrowIfNull(value);
		_metadata[key] = value;
		return this;
	}

	/// <summary>
	/// Declares an interactive answer slot that can be pre-filled via <c>--answer:{name}=value</c>
	/// on the CLI or <c>answer:{name}</c> in MCP tool calls.
	/// </summary>
	/// <param name="name">Answer name (matches the <c>name</c> parameter in <c>AskConfirmationAsync</c>, <c>AskChoiceAsync</c>, etc.).</param>
	/// <param name="type">Value type using route constraint names: <c>string</c>, <c>bool</c>, <c>int</c>, <c>guid</c>, <c>email</c>, etc.</param>
	/// <param name="description">Optional description for help text and agent tool schemas.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithAnswer(string name, string type = "string", string? description = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		_answers.Add(new AnswerDeclaration(name, type, description));
		return this;
	}

	// ── Annotation shortcuts ───────────────────────────────────────────
	// Same style as Hidden() — short, chainable, directly on CommandBuilder.
	// Uses `with` expressions to preserve record immutability.

	/// <summary>Marks the command as read-only (no side effects).</summary>
	/// <param name="value">True to mark as read-only.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder ReadOnly(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { ReadOnly = value };
		return this;
	}

	/// <summary>Marks the command as destructive (deletes, modifies state).</summary>
	/// <param name="value">True to mark as destructive.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder Destructive(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { Destructive = value };
		return this;
	}

	/// <summary>Marks the command as safely retriable.</summary>
	/// <param name="value">True to mark as idempotent.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder Idempotent(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { Idempotent = value };
		return this;
	}

	/// <summary>Marks the command as interacting with external systems.</summary>
	/// <param name="value">True to mark as open-world.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder OpenWorld(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { OpenWorld = value };
		return this;
	}

	/// <summary>Marks the command as long-running (enables task-based execution).</summary>
	/// <param name="value">True to mark as long-running.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder LongRunning(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { LongRunning = value };
		return this;
	}

	/// <summary>Hides the command from programmatic/automation surfaces only.</summary>
	/// <param name="value">True to hide from automation.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder AutomationHidden(bool value = true)
	{
		Annotations = (Annotations ?? new CommandAnnotations()) with { AutomationHidden = value };
		_invalidateRouting?.Invoke(value);
		return this;
	}

	/// <summary>
	/// Configures annotations via builder — escape hatch for complex scenarios.
	/// Overwrites any annotations set by individual shortcuts.
	/// </summary>
	/// <param name="configure">Builder configuration callback.</param>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder WithAnnotations(Action<CommandAnnotationsBuilder> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		var builder = new CommandAnnotationsBuilder();
		configure(builder);
		Annotations = builder.Build();
		_invalidateRouting?.Invoke(Annotations.AutomationHidden);
		return this;
	}

	// ── Semantic markers ───────────────────────────────────────────────

	/// <summary>
	/// Marks this command as a resource (data to consult, not an operation to perform).
	/// Resources appear in a separate help section and are auto-exposed as MCP resources.
	/// </summary>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder AsResource()
	{
		IsResource = true;
		return this;
	}

	/// <summary>
	/// Marks this command as a prompt source.
	/// The handler return value becomes the prompt message template.
	/// Handler parameters become prompt arguments.
	/// </summary>
	/// <returns>The same builder instance.</returns>
	public CommandBuilder AsPrompt()
	{
		IsPrompt = true;
		return this;
	}

	private static bool ComputeSupportsHostedProtocolPassthrough(Delegate handler)
	{
		foreach (var parameter in handler.Method.GetParameters())
		{
			if (parameter.ParameterType != typeof(IReplIoContext))
			{
				continue;
			}

			// [FromContext] binds route/context values and is not stream injection.
			if (parameter.GetCustomAttributes(typeof(FromContextAttribute), inherit: true).Length > 0)
			{
				continue;
			}

			return true;
		}

		return false;
	}
}
