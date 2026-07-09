using Repl.Internal.Options;

namespace Repl;

/// <summary>
/// Provides shell (bash/zsh/fish/etc.) completion candidates for the Repl routing graph.
/// </summary>
internal sealed class ShellCompletionEngine(CoreReplApp app)
{

	public string[] ResolveShellCompletionCandidates(string line, int cursor)
	{
		var activeGraph = app.ResolveActiveRoutingGraph();
		var state = AnalyzeShellCompletionInput(line, cursor);
		if (state.PriorTokens.Length == 0)
		{
			return [];
		}

		var optionsTerminated = false;
		var commandPrefix = BuildShellCommandPrefix(state.PriorTokens, activeGraph, ref optionsTerminated);
		var currentTokenPrefix = state.CurrentTokenPrefix;
		// Same gate as the interactive menu: single-dash prefixes surface short option
		// aliases (-f); signed numeric literals stay positional. After the POSIX "--"
		// separator no option names may be offered — everything is positional.
		var currentTokenIsOption = !optionsTerminated && AutocompleteEngine.IsOptionPrefixToken(currentTokenPrefix);
		var routeMatch = app.Resolve(commandPrefix, activeGraph.Routes);
		var hasTerminalRoute = routeMatch is not null && routeMatch.RemainingTokens.Count == 0;
		var dedupe = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var candidates = new List<string>(capacity: 16);
		if (!currentTokenIsOption
			&& !optionsTerminated
			&& hasTerminalRoute
			&& TryAddRouteEnumValueCandidates(
				routeMatch!.Route,
				state.PriorTokens,
				currentTokenPrefix,
				dedupe,
				candidates))
		{
			candidates.Sort(StringComparer.OrdinalIgnoreCase);
			return [.. candidates];
		}

		if (!currentTokenIsOption)
		{
			AddShellCommandCandidates(
				commandPrefix,
				currentTokenPrefix,
				activeGraph.Routes,
				activeGraph.Contexts,
				dedupe,
				candidates);
		}

		if (!optionsTerminated && (currentTokenIsOption || (string.IsNullOrEmpty(currentTokenPrefix) && hasTerminalRoute)))
		{
			AddShellOptionCandidates(
				hasTerminalRoute ? routeMatch!.Route : null,
				currentTokenPrefix,
				dedupe,
				candidates);
		}

		candidates.Sort(StringComparer.OrdinalIgnoreCase);
		return [.. candidates];
	}

	// The first prior token is the executable name. The rest reduces to the positional
	// command prefix the way execution does — without guessing option arities: global
	// options are stripped by the parser that knows their arities, the bare "--" separator
	// is recorded and removed, then the route is resolved so a matched route contributes
	// exactly its own segments (a valued short alias like "-f value" leaves both tokens in
	// the route's trailing options, never in the prefix).
	private string[] BuildShellCommandPrefix(
		string[] priorTokens,
		ActiveRoutingGraph activeGraph,
		ref bool optionsTerminated)
	{
		if (priorTokens.Length <= 1)
		{
			return [];
		}

		var afterExecutable = new ArraySegment<string>(priorTokens, offset: 1, count: priorTokens.Length - 1);
		var stripped = GlobalOptionParser
			.Parse(afterExecutable, app.OptionsSnapshot.Output, app.OptionsSnapshot.Parsing)
			.RemainingTokens;

		var positional = new List<string>(stripped.Count);
		foreach (var token in stripped)
		{
			if (string.Equals(token, "--", StringComparison.Ordinal))
			{
				optionsTerminated = true;
				continue;
			}

			positional.Add(token);
		}

		var prefix = positional.ToArray();
		if (app.Resolve(prefix, activeGraph.Routes) is { } match)
		{
			return prefix[..match.Route.Template.Segments.Count];
		}

		var commandWords = new List<string>(prefix.Length);
		foreach (var token in prefix)
		{
			if (!AutocompleteEngine.IsOptionPrefixToken(token))
			{
				commandWords.Add(token);
			}
		}

		return [.. commandWords];
	}

	private bool TryAddRouteEnumValueCandidates(
		RouteDefinition route,
		string[] priorTokens,
		string currentTokenPrefix,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		if (!TryResolvePendingRouteOption(route, priorTokens, out var entry))
		{
			return false;
		}

		if (!route.OptionSchema.TryGetParameter(entry.ParameterName, out var parameter))
		{
			return false;
		}

		var enumType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
		if (!enumType.IsEnum)
		{
			return false;
		}

		var effectiveCaseSensitivity = parameter.CaseSensitivity ?? app.OptionsSnapshot.Parsing.OptionCaseSensitivity;
		var comparison = effectiveCaseSensitivity == ReplCaseSensitivity.CaseInsensitive
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;
		var beforeCount = candidates.Count;
		foreach (var enumName in Enum
			         .GetNames(enumType)
			         .Where(name => name.StartsWith(currentTokenPrefix, comparison)))
		{
			TryAddShellCompletionCandidate(enumName, dedupe, candidates);
		}

		return candidates.Count > beforeCount;
	}

	private bool TryResolvePendingRouteOption(
		RouteDefinition route,
		string[] priorTokens,
		out OptionSchemaEntry entry)
	{
		entry = default!;
		if (priorTokens.Length <= 1)
		{
			return false;
		}

		var commandTokens = priorTokens[1..];
		if (commandTokens.Length == 0)
		{
			return false;
		}

		var previousToken = commandTokens[^1];
		if (!AutocompleteEngine.IsGlobalOptionToken(previousToken))
		{
			return false;
		}

		var separatorIndex = previousToken.IndexOfAny(['=', ':']);
		if (separatorIndex >= 0)
		{
			return false;
		}

		var matches = route.OptionSchema.ResolveToken(previousToken, app.OptionsSnapshot.Parsing.OptionCaseSensitivity);
		var distinct = matches
			.DistinctBy(candidate => (candidate.ParameterName, candidate.TokenKind, candidate.InjectedValue), ShellOptionSchemaEntryComparer.Instance)
			.ToArray();
		if (distinct.Length != 1)
		{
			return false;
		}

		if (distinct[0].TokenKind is not (OptionSchemaTokenKind.NamedOption or OptionSchemaTokenKind.BoolFlag))
		{
			return false;
		}

		entry = distinct[0];
		return true;
	}

	private static void TryAddShellCompletionCandidate(
		string candidate,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		if (string.IsNullOrWhiteSpace(candidate) || !dedupe.Add(candidate))
		{
			return;
		}

		candidates.Add(candidate);
	}

	private void AddShellCommandCandidates(
		string[] commandPrefix,
		string currentTokenPrefix,
		IReadOnlyList<RouteDefinition> routes,
		IReadOnlyList<ContextDefinition> contexts,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		var matchingRoutes = app.Autocomplete.CollectVisibleMatchingRoutes(
			commandPrefix,
			StringComparison.OrdinalIgnoreCase,
			routes,
			contexts);
		foreach (var route in matchingRoutes)
		{
			if (commandPrefix.Length >= route.Template.Segments.Count
				|| route.Template.Segments[commandPrefix.Length] is not LiteralRouteSegment literal
				|| !literal.Value.StartsWith(currentTokenPrefix, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			TryAddShellCompletionCandidate(literal.Value, dedupe, candidates);
		}
	}

	private void AddShellOptionCandidates(
		RouteDefinition? route,
		string currentTokenPrefix,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		AddGlobalShellOptionCandidates(currentTokenPrefix, dedupe, candidates);

		if (route is null)
		{
			return;
		}

		AddRouteShellOptionCandidates(route, currentTokenPrefix, dedupe, candidates);
	}

	private void AddGlobalShellOptionCandidates(
		string currentTokenPrefix,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		var options = app.OptionsSnapshot;
		OptionTokenCompletionSource.CollectGlobalOptionTokens(
			options,
			currentTokenPrefix,
			options.Parsing.OptionCaseSensitivity.ToStringComparison(),
			dedupe,
			candidates);
	}

	private void AddRouteShellOptionCandidates(
		RouteDefinition route,
		string currentTokenPrefix,
		HashSet<string> dedupe,
		List<string> candidates)
	{
		OptionTokenCompletionSource.CollectRouteOptionTokens(
			route,
			currentTokenPrefix,
			app.OptionsSnapshot.Parsing.OptionCaseSensitivity.ToStringComparison(),
			dedupe,
			candidates);
	}

	internal static ShellCompletionInputState AnalyzeShellCompletionInput(string input, int cursor)
	{
		input ??= string.Empty;
		cursor = Math.Clamp(cursor, 0, input.Length);
		var tokens = AutocompleteEngine.TokenizeInputSpans(input);
		for (var i = 0; i < tokens.Count; i++)
		{
			var token = tokens[i];
			if (cursor < token.Start || cursor > token.End)
			{
				continue;
			}

			var prior = new string[i];
			for (var priorIndex = 0; priorIndex < i; priorIndex++)
			{
				prior[priorIndex] = tokens[priorIndex].Value;
			}

			var prefix = input[token.Start..cursor];
			return new ShellCompletionInputState(prior, prefix);
		}

		var trailingPriorCount = 0;
		foreach (var token in tokens)
		{
			if (token.End <= cursor)
			{
				trailingPriorCount++;
			}
		}

		if (trailingPriorCount == 0)
		{
			return new ShellCompletionInputState([], CurrentTokenPrefix: string.Empty);
		}

		var trailingPrior = new string[trailingPriorCount];
		var index = 0;
		foreach (var token in tokens)
		{
			if (token.End <= cursor)
			{
				trailingPrior[index++] = token.Value;
			}
		}

		return new ShellCompletionInputState(trailingPrior, CurrentTokenPrefix: string.Empty);
	}

	internal readonly record struct ShellCompletionInputState(
		string[] PriorTokens,
		string CurrentTokenPrefix);

	internal static string ResolveShellCompletionCommandName(
		IReadOnlyList<string>? commandLineArgs,
		string? processPath,
		string? fallbackName)
	{
		if (commandLineArgs is { Count: > 0 })
		{
			var commandHead = TryGetCommandHead(commandLineArgs[0]);
			if (!string.IsNullOrWhiteSpace(commandHead))
			{
				return commandHead;
			}
		}

		var processHead = TryGetCommandHead(processPath);
		if (!string.IsNullOrWhiteSpace(processHead))
		{
			return processHead;
		}

		return string.IsNullOrWhiteSpace(fallbackName) ? "repl" : fallbackName;
	}

	private static string? TryGetCommandHead(string? pathLike)
	{
		if (string.IsNullOrWhiteSpace(pathLike))
		{
			return null;
		}

		var fileName = Path.GetFileName(pathLike.Trim());
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return null;
		}

		foreach (var extension in KnownExecutableExtensions)
		{
			if (fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
			{
				var head = fileName[..^extension.Length];
				return string.IsNullOrWhiteSpace(head) ? null : head;
			}
		}

		return fileName;
	}

	private static readonly string[] KnownExecutableExtensions =
	[
		".exe",
		".cmd",
		".bat",
		".com",
		".ps1",
		".dll",
	];

	public string ResolveShellCompletionCommandName()
	{
		var docApp = app.BuildDocumentationApp();
		return ResolveShellCompletionCommandName(
			Environment.GetCommandLineArgs(),
			Environment.ProcessPath,
			docApp.Name);
	}

	private sealed class ShellOptionSchemaEntryComparer : IEqualityComparer<(string ParameterName, OptionSchemaTokenKind TokenKind, string? InjectedValue)>
	{
		public static ShellOptionSchemaEntryComparer Instance { get; } = new();

		public bool Equals(
			(string ParameterName, OptionSchemaTokenKind TokenKind, string? InjectedValue) x,
			(string ParameterName, OptionSchemaTokenKind TokenKind, string? InjectedValue) y) =>
			string.Equals(x.ParameterName, y.ParameterName, StringComparison.OrdinalIgnoreCase)
			&& x.TokenKind == y.TokenKind
			&& string.Equals(x.InjectedValue, y.InjectedValue, StringComparison.Ordinal);

		public int GetHashCode((string ParameterName, OptionSchemaTokenKind TokenKind, string? InjectedValue) obj)
		{
			var parameterHash = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ParameterName);
			var injectedHash = obj.InjectedValue is null
				? 0
				: StringComparer.Ordinal.GetHashCode(obj.InjectedValue);
			return HashCode.Combine(parameterHash, (int)obj.TokenKind, injectedHash);
		}
	}
}
