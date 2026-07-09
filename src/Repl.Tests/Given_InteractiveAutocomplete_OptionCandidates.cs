namespace Repl.Tests;

[TestClass]
public sealed class Given_InteractiveAutocomplete_OptionCandidates
{
	[TestMethod]
	[Description("Interactive autocomplete suggests route and global option names when the current token is an option prefix.")]
	public async Task When_CurrentTokenIsOptionPrefix_Then_SuggestsRouteAndGlobalOptions()
	{
		var sut = CoreReplApp.Create();
		sut.Options(options => options.Parsing.AddGlobalOption<string>("tenant", aliases: ["--org"]));
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("--force");
		values.Should().Contain("--help");
		values.Should().Contain("--json");
		values.Should().Contain("--tenant");
		values.Should().Contain("--org");
		result.Suggestions.Should().OnlyContain(static suggestion => suggestion.IsSelectable);
		result.HintLine.Should().Contain("--force");
	}

	[TestMethod]
	[Description("Interactive autocomplete filters option suggestions by a partial option prefix.")]
	public async Task When_CurrentTokenIsPartialOptionPrefix_Then_FiltersOptionSuggestions()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls --fo").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().ContainSingle().Which.Should().Be("--force");
		result.ReplaceStart.Should().Be("install bib-overalls ".Length);
		result.ReplaceLength.Should().Be("--fo".Length);
		result.HintLine.Should().Be("--force");
	}

	[TestMethod]
	[Description("A valued option given in its space-separated form must not break route matching: the option's VALUE is consumed like the invocation parser does, so '--channel beta' does not leave a stray positional that hides the route's own options.")]
	public async Task When_ValuedOptionPrecedesOptionPrefix_Then_RouteOptionsAreStillSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
				"install {skillName}",
				static string (string skillName, [ReplOption] bool force, [ReplOption] string? channel) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls --channel beta --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("--force", because: "consuming '--channel beta' leaves the same command prefix the router sees");
	}

	[TestMethod]
	[Description("Command aliases resolve for option suggestions like they do for execution: invoking an aliased command ('i' for 'install') must surface the route's options, mirroring RouteResolver's terminal-segment alias matching.")]
	public async Task When_CommandIsInvokedThroughAlias_Then_RouteOptionsAreSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithAlias("i")
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "i bib-overalls --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("--force", because: "the router accepts the alias, so autocomplete must too");
	}

	[TestMethod]
	[Description("A single dash is already an option prefix: short option aliases declared via [ReplOption(Aliases = [\"-f\"])] must be suggested when the user types '-'.")]
	public async Task When_CurrentTokenIsSingleDash_Then_ShortOptionAliasesAreSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
				"install {skillName}",
				static string (string skillName, [ReplOption(Aliases = ["-f"])] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls -").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("-f", because: "declared short aliases are reachable only from a single-dash prefix");
		values.Should().Contain("--force");
	}

	[TestMethod]
	[Description("Options are accepted anywhere by the parser, so route options must be offered as soon as the command WORDS are fully typed — even when positional arguments are still missing ('install --' must offer --force).")]
	public async Task When_PositionalArgumentsAreStillMissing_Then_RouteOptionsAreSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("--force", because: "the parser accepts options before trailing positionals");
	}

	[TestMethod]
	[Description("A bare '--' prior token is the POSIX end-of-options separator: everything after it is positional, so no option name may be suggested past it.")]
	public async Task When_EndOfOptionsSeparatorPrecedes_Then_NoOptionIsSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls -- --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().NotContain("--force", because: "tokens after the end-of-options separator are positional");
		values.Should().NotContain("--help");
	}

	[TestMethod]
	[Description("A signed numeric literal is a positional argument, not an option prefix: typing '-4' must not open the option menu (mirrors the invocation parser's IsSignedNumericLiteral rule).")]
	public async Task When_CurrentTokenIsNegativeNumber_Then_NoOptionIsSuggested()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls -4").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().NotContain("--force");
	}

	[TestMethod]
	[Description("Dynamic completion providers complete parameter VALUES; when the current token is an option prefix the provider must not run, otherwise its values pollute the option menu.")]
	public async Task When_CurrentTokenIsOptionPrefix_Then_DynamicCompletionProviderIsSkipped()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithCompletion("skillName", static (_, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["ga", "bu", "zo", "meu"]))
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls --").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().NotContain("ga", because: "provider values are parameter values, not option names");
		values.Should().Contain("--force");
	}

	[TestMethod]
	[Description("An option name shared by a global option and a route option appears exactly once in the menu — the dedup set spans both sources.")]
	public async Task When_GlobalAndRouteOptionsCollide_Then_SuggestionAppearsOnce()
	{
		var sut = CoreReplApp.Create();
		sut.Options(options => options.Parsing.AddGlobalOption<string>("force"));
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install bib-overalls --fo").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Count(static value => string.Equals(value, "--force", StringComparison.OrdinalIgnoreCase))
			.Should().Be(1);
	}

	[TestMethod]
	[Description("In option-prefix mode the live hint must describe the option alternatives, not the pending positional parameter: 'install --' lists --force/--help in the menu, so a 'Param skillName' hint would contradict what the user is looking at.")]
	public async Task When_OptionPrefixWithMissingPositional_Then_HintShowsOptionsNotParameter()
	{
		var sut = CoreReplApp.Create();
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");

		var result = await ResolveAutocompleteAsync(sut, "install --").ConfigureAwait(false);

		result.HintLine.Should().NotContain("skillName", because: "the user asked for options, not the positional parameter");
		result.HintLine.Should().Contain("--force");
	}

	[TestMethod]
	[Description("Autocomplete must NEVER touch the filesystem: a '@file' prior token stays a literal token instead of being expanded as a response file. Hosted sessions feed remote-controlled lines through this path on every keystroke — expansion would mean server-side file reads (UNC probes included) driven by keystrokes.")]
	public async Task When_PriorTokenLooksLikeResponseFile_Then_ItIsNotExpanded()
	{
		var responseFile = Path.GetTempFileName();
		try
		{
			// If the parser expanded this, the prefix would gain three tokens and the
			// route would no longer match — observable as the route options vanishing.
			await File.WriteAllTextAsync(responseFile, "ga bu zo").ConfigureAwait(false);
			var sut = CoreReplApp.Create();
			sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
				.WithDescription("Install a skill.");

			var result = await ResolveAutocompleteAsync(sut, $"install @{responseFile} --").ConfigureAwait(false);

			var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
			values.Should().Contain("--force", because: "the @token must be treated as a plain positional, not expanded from disk");
		}
		finally
		{
			File.Delete(responseFile);
		}
	}

	[TestMethod]
	[Description("Tokens after the end-of-options separator are positional even when they look like flags: in 'deploy -- -f ' the '-f' fills the {target} segment, so the exact-route completion provider must fire — dropping it would desync the segment count.")]
	public async Task When_DashTokenFollowsSeparator_Then_ItCountsAsPositional()
	{
		var sut = CoreReplApp.Create();
		sut.Map("deploy {target}", static string (string target) => target)
			.WithCompletion("target", static (_, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["zo-profile"]))
			.WithDescription("Deploy a target.");

		var result = await ResolveAutocompleteAsync(sut, "deploy -- -f ").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().Contain("zo-profile", because: "'-f' after '--' is a positional filling {target}, making the route exact for the provider");
	}

	[TestMethod]
	[Description("Parity contract between the two completion surfaces: for the same app and the same '--' input, the interactive menu and the shell-completion candidates carry the same option tokens. The shared token source is only half the guarantee — the gates must agree too, and this pin catches a divergent reimplementation on either side.")]
	public async Task When_ComparingInteractiveAndShellOnDoubleDash_Then_CandidatesMatch()
	{
		var sut = CoreReplApp.Create();
		sut.Options(options => options.Parsing.AddGlobalOption<string>("tenant", aliases: ["--org"]));
		sut.Map("install {skillName}", static string (string skillName, [ReplOption] bool force) => skillName)
			.WithDescription("Install a skill.");
		var shellEngine = new ShellCompletionEngine(sut);
		const string shellLine = "app install bib-overalls --";

		var interactive = await ResolveAutocompleteAsync(sut, "install bib-overalls --").ConfigureAwait(false);
		var shell = shellEngine.ResolveShellCompletionCandidates(shellLine, shellLine.Length);

		var interactiveOptions = interactive.Suggestions
			.Where(static suggestion => suggestion.IsSelectable)
			.Select(static suggestion => suggestion.Value)
			.ToArray();
		interactiveOptions.Should().BeEquivalentTo(shell);
	}

	[TestMethod]
	[Description("Parity contract for the single-dash gate: shell completion must surface short option aliases (-f) from '-' exactly like the interactive menu does — the two surfaces answering the same question differently is operator-visible confusion.")]
	public async Task When_ComparingInteractiveAndShellOnSingleDash_Then_CandidatesMatch()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
				"install {skillName}",
				static string (string skillName, [ReplOption(Aliases = ["-f"])] bool force) => skillName)
			.WithDescription("Install a skill.");
		var shellEngine = new ShellCompletionEngine(sut);
		const string shellLine = "app install bib-overalls -";

		var interactive = await ResolveAutocompleteAsync(sut, "install bib-overalls -").ConfigureAwait(false);
		var shell = shellEngine.ResolveShellCompletionCandidates(shellLine, shellLine.Length);

		var interactiveOptions = interactive.Suggestions
			.Where(static suggestion => suggestion.IsSelectable)
			.Select(static suggestion => suggestion.Value)
			.ToArray();
		interactiveOptions.Should().Contain("-f");
		interactiveOptions.Should().BeEquivalentTo(shell);
	}

	private static async Task<ConsoleLineReader.AutocompleteResult> ResolveAutocompleteAsync(CoreReplApp app, string input)
	{
		var result = await app.Autocomplete.ResolveAutocompleteAsync(
			new ConsoleLineReader.AutocompleteRequest(input, input.Length, MenuRequested: true),
			scopeTokens: [],
			EmptyServiceProvider.Instance,
			CancellationToken.None)
			.ConfigureAwait(false);
		result.Should().NotBeNull();
		return result!.Value;
	}

	private sealed class EmptyServiceProvider : IServiceProvider
	{
		public static readonly EmptyServiceProvider Instance = new();
		public object? GetService(Type serviceType) => null;
	}
}
