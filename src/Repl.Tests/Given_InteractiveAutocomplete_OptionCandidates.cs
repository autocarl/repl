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

		var result = await ResolveAutocompleteAsync(sut, "install msaf-architect --").ConfigureAwait(false);

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

		var result = await ResolveAutocompleteAsync(sut, "install msaf-architect --fo").ConfigureAwait(false);

		var values = result.Suggestions.Select(static suggestion => suggestion.Value).ToArray();
		values.Should().ContainSingle().Which.Should().Be("--force");
		result.ReplaceStart.Should().Be("install msaf-architect ".Length);
		result.ReplaceLength.Should().Be("--fo".Length);
		result.HintLine.Should().Be("--force");
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
