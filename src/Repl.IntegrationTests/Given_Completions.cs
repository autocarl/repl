using System.Globalization;

namespace Repl.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class Given_Completions
{
	[TestMethod]
	[Description("Regression guard: verifies interactive complete command is used so that completion provider candidates are rendered.")]
	public void When_InteractiveCompleteCommandIsUsed_Then_CompletionProviderCandidatesAreRendered()
	{
		var sut = ReplApp.Create().UseDefaultInteractive();
		sut.Map("contact inspect", () => "ok")
			.WithCompletion("clientId", static (_, input, _) =>
				ValueTask.FromResult<IReadOnlyList<string>>(
					[$"{input}001", $"{input}002"]));

		var output = ConsoleCaptureHelper.CaptureWithInput(
			"complete contact inspect --target clientId --input ab\nexit\n",
			() => sut.Run([]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("ab001");
		output.Text.Should().Contain("ab002");
	}

	[TestMethod]
	[Description("A hidden option's completion provider must not be reachable through the complete ambient command, and the refusal must not reveal that the option exists. Asserting the wording is identical to the unknown-target refusal — bar the name — makes the non-disclosure a contract instead of a coincidence, and stops the message from claiming no provider is registered when one is.")]
	public void When_InteractiveCompleteTargetsHiddenOption_Then_ItIsIndistinguishableFromAnUnknownTarget()
	{
		var sut = ReplApp.Create().UseDefaultInteractive();
		var command = sut.Map(
			"deploy",
			static string ([ReplOption(Name = "secret-mode")] string? secretMode = null) => secretMode ?? "none");
		command.WithCompletion(
			"secretMode",
			static (_, _, _) => ValueTask.FromResult<IReadOnlyList<string>>(["internal-debug"]));
		command.WithOption("secretMode", option => option.Hidden());

		var hidden = ConsoleCaptureHelper.CaptureWithInput(
			"complete deploy --target secretMode\nexit\n",
			() => sut.Run([]));
		var unknown = ConsoleCaptureHelper.CaptureWithInput(
			"complete deploy --target ga-bu-zo-meu\nexit\n",
			() => sut.Run([]));

		hidden.ExitCode.Should().Be(0);
		hidden.Text.Should().Contain("Error: no completion is available for 'secretMode'.");
		hidden.Text.Should().NotContain("internal-debug");
		unknown.Text.Should().Contain("Error: no completion is available for 'ga-bu-zo-meu'.");
	}

	[TestMethod]
	[Description("Regression guard: verifies interactive complete command uses unknown target so that error is rendered.")]
	public void When_InteractiveCompleteCommandUsesUnknownTarget_Then_ErrorIsRendered()
	{
		var sut = ReplApp.Create().UseDefaultInteractive();
		sut.Map("contact inspect", () => "ok");

		var output = ConsoleCaptureHelper.CaptureWithInput(
			"complete contact inspect --target missing\nexit\n",
			() => sut.Run([]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("Error: no completion is available for 'missing'.");
	}

	[TestMethod]
	[Description("Regression guard: verifies cli complete command is used so that completion provider candidates are rendered.")]
	public void When_CliCompleteCommandIsUsed_Then_CompletionProviderCandidatesAreRendered()
	{
		var sut = ReplApp.Create();
		sut.Map("contact inspect", () => "ok")
			.WithCompletion("clientId", static (_, input, _) =>
				ValueTask.FromResult<IReadOnlyList<string>>([$"{input}A", $"{input}B"]));

		var output = ConsoleCaptureHelper.Capture(() =>
			sut.Run(["complete", "contact", "inspect", "--target", "clientId", "--input", "x"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("xA");
		output.Text.Should().Contain("xB");
	}

	[TestMethod]
	[Description("Regression guard: verifies interactive autocomplete show command reports configured and effective mode.")]
	public void When_AutocompleteShowCommandIsUsed_Then_ModeSummaryIsRendered()
	{
		var sut = ReplApp.Create().UseDefaultInteractive();
		sut.Map("ping", () => "pong");

		var output = ConsoleCaptureHelper.CaptureWithInput(
			"autocomplete show\nexit\n",
			() => sut.Run([]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("Autocomplete mode:");
	}

	[TestMethod]
	[Description("Regression guard: verifies interactive autocomplete mode command stores a session override.")]
	public void When_AutocompleteModeCommandIsUsed_Then_SessionOverrideIsApplied()
	{
		var sut = ReplApp.Create().UseDefaultInteractive();
		sut.Map("ping", () => "pong");

		var output = ConsoleCaptureHelper.CaptureWithInput(
			"autocomplete mode off\nautocomplete show\nexit\n",
			() => sut.Run([]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("Autocomplete mode set to Off");
		output.Text.Should().Contain("override=Off");
	}

	[TestMethod]
	[Description("Hidden custom global options and their aliases are omitted from shell completion while visible framework options remain discoverable.")]
	public void When_CompletingGlobalPrefixWithHiddenCustomOption_Then_HiddenTokensAreNotSuggested()
	{
		var sut = ReplApp.Create()
			.Options(options =>
			{
				options.Parsing.AddGlobalOption<string>("region", aliases: ["-r"]);
				options.Parsing.AddGlobalOption<string>("tenant", aliases: ["-t"]);
				options.Parsing.GlobalOption("tenant").Hidden();
			});
		sut.Map("ping", () => "pong");

		var longCandidates = Complete("repl --");
		var shortCandidates = Complete("repl -");

		longCandidates.Should().Contain("--help");
		longCandidates.Should().NotContain("--tenant");
		shortCandidates.Should().Contain("-r");
		shortCandidates.Should().NotContain("-t");

		string[] Complete(string line)
		{
			var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			[
				"completion",
				"__complete",
				"--shell",
				"bash",
				"--line",
				line,
				"--cursor",
				line.Length.ToString(CultureInfo.InvariantCulture),
				"--no-logo",
			]));

			output.ExitCode.Should().Be(0);

			// The completion emitter separates candidates with '\n', but the final line is flushed
			// with Environment.NewLine — so on Windows the last candidate carries a trailing '\r'.
			// TrimEntries strips it; without it the last candidate never compares equal, which made
			// this test pass on the Linux/macOS CI legs and fail only on Windows.
			return output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		}
	}

	[TestMethod]
	[Description("A token registered as a visible option's alias and later as a hidden option's canonical form belongs, per GlobalOptionParser, to the LAST registration — the hidden one. Enumerating definitions independently would advertise the token from the visible definition while accepting it would bind the hidden one, which is the mismatch that file's own comment warns callers against.")]
	public void When_AVisibleGlobalAliasCollidesWithALaterHiddenOption_Then_TheTokenIsNotSuggested()
	{
		var sut = ReplApp.Create()
			.Options(options =>
			{
				options.Parsing.AddGlobalOption<string>("region", aliases: ["--tenant"]);
				options.Parsing.AddGlobalOption<string>("tenant");
				options.Parsing.GlobalOption("tenant").Hidden();
			});
		sut.Map("ping", () => "pong");
		const string line = "repl --t";

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
		[
			"completion",
			"__complete",
			"--shell",
			"bash",
			"--line",
			line,
			"--cursor",
			line.Length.ToString(CultureInfo.InvariantCulture),
			"--no-logo",
		]));

		output.ExitCode.Should().Be(0);
		output.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Should().NotContain("--tenant");
	}

	[TestMethod]
	[Description("In case-insensitive mode, a losing hidden token that differs only by case must not enter deduplication before the later visible owner. Completion must emit the winning definition's spelling, matching parser/help ownership and registration order.")]
	public void When_AVisibleGlobalClaimsACaseVariantOfAHiddenAlias_Then_CompletionUsesTheVisibleSpelling()
	{
		var sut = ReplApp.Create()
			.Options(options =>
			{
				options.Parsing.OptionCaseSensitivity = ReplCaseSensitivity.CaseInsensitive;
				options.Parsing.AddGlobalOption<string>("legacy", aliases: ["--TENANT"]);
				options.Parsing.GlobalOption("legacy").Hidden();
				options.Parsing.AddGlobalOption<string>("tenant");
			});
		sut.Map("ping", () => "pong");
		const string line = "repl --t";

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
		[
			"completion",
			"__complete",
			"--shell",
			"bash",
			"--line",
			line,
			"--cursor",
			line.Length.ToString(CultureInfo.InvariantCulture),
			"--no-logo",
		]));
		var candidates = output.Text.Split(
			'\n',
			StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

		output.ExitCode.Should().Be(0);
		candidates.Should().ContainSingle().Which.Should().Be("--tenant");
	}

	[TestMethod]
	[Description("Regression guard: verifies shell completion suggests custom global options so app-registered globals remain discoverable from tab completion.")]
	public void When_CompletingGlobalPrefix_Then_CustomGlobalOptionsAreSuggested()
	{
		var sut = ReplApp.Create()
			.Options(options => options.Parsing.AddGlobalOption<string>("tenant", aliases: ["-t"]));
		sut.Map("ping", () => "pong");
		const string line = "repl --te";

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
		[
			"completion",
			"__complete",
			"--shell",
			"bash",
			"--line",
			line,
			"--cursor",
			line.Length.ToString(CultureInfo.InvariantCulture),
			"--no-logo",
		]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("--tenant");
	}
}


