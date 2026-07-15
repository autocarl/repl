namespace Repl.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class Given_OptionAttributeOverrides
{
	private enum ShadockSyllable
	{
		None = 0,

		[ReplEnumFlag("--ga", CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)]
		Ga = 1,

		[ReplEnumFlag("--bu")]
		Bu = 2,
	}

	[ReplOptionsGroup]
	public sealed class DenimOutfitOptions
	{
		[ReplOption(CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)]
		public string Fabric { get; set; } = "denim";

		[ReplOption(Arity = ReplArity.ZeroOrOne)]
		public string[] Patches { get; set; } = [];
	}

	public sealed class OverrideGlobals
	{
		[ReplOption(Name = "tenant", CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)]
		public string Tenant { get; set; } = "ga";
	}

	[TestMethod]
	[Description("Regression guard for issue #57: [ReplOption(CaseSensitivity = ...)] must be a legal attribute argument (a nullable enum triggers CS0655, making the override dead code) and the per-option override must accept casing variants while the global default stays case-sensitive.")]
	public void When_OptionCaseSensitivityOverriddenViaAttribute_Then_CasingVariantIsAccepted()
	{
		var sut = ReplApp.Create();
		sut.Map("echo", ([ReplOption(CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)] string text) => text);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["echo", "--Text", "bib overalls", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("bib overalls");
	}

	[TestMethod]
	[Description("Regression guard for issue #57 (expanded): [ReplOption(Arity = ...)] must be a legal attribute argument. A collection parameter naturally allows repetition (ZeroOrMore); the explicit ZeroOrOne override must be honored so a repeated option is rejected at parse time.")]
	public void When_ArityOverriddenViaAttributeToZeroOrOne_Then_RepeatedOptionIsRejected()
	{
		var sut = ReplApp.Create();
		sut.Map("echo", ([ReplOption(Arity = ReplArity.ZeroOrOne)] string[] items) => string.Join(',', items));

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["echo", "--items", "ga", "--items", "bu", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("accepts at most one value");
	}

	[TestMethod]
	[Description("Regression guard for issue #57 (expanded): [ReplValueAlias(..., CaseSensitivity = ...)] must be a legal attribute argument so an alias token matched ignoring case still injects its configured value.")]
	public void When_ValueAliasCaseSensitivityOverriddenViaAttribute_Then_CasingVariantInjectsValue()
	{
		var sut = ReplApp.Create();
		sut.Map("wear", ([ReplValueAlias("--denim", "bib overalls", CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)] string outfit = "none") => outfit);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["wear", "--DENIM", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("bib overalls");
	}

	[TestMethod]
	[Description("Regression guard for issue #57 (expanded): [ReplEnumFlag(..., CaseSensitivity = ...)] must be a legal attribute argument so an enum-flag alias matched ignoring case binds the enum member while other members stay case-sensitive.")]
	public void When_EnumFlagCaseSensitivityOverriddenViaAttribute_Then_CasingVariantBindsEnumMember()
	{
		var sut = ReplApp.Create();
		sut.Map("say", (ShadockSyllable syllable = ShadockSyllable.None) => syllable.ToString());

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["say", "--GA", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("Ga");
	}

	[TestMethod]
	[Description("Guards the ambiguity edge unlocked by issue #57: a typed token matching both a case-insensitive alias and another option's case-sensitive canonical token must be rejected as ambiguous at parse time, never silently bound to either parameter.")]
	public void When_TokenMatchesCaseInsensitiveAliasAndAnotherOption_Then_AmbiguityIsRejected()
	{
		var sut = ReplApp.Create();
		sut.Map(
			"probe",
			([ReplOption(Aliases = ["--Mode"], CaseSensitivity = ReplCaseSensitivity.CaseInsensitive)] string first = "ga",
			 [ReplOption(Name = "mode")] string second = "bu") => $"first={first};second={second}");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["probe", "--mode", "zo", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("Ambiguous option '--mode'");
	}

	[TestMethod]
	[Description("Guards the override boundary: a sibling enum-flag alias without a case-sensitivity override must keep the global case-sensitive default, so per-member overrides stay scoped to their own aliases.")]
	public void When_EnumFlagWithoutOverrideAndCasingDiffers_Then_TokenIsRejected()
	{
		var sut = ReplApp.Create();
		sut.Map("say", (ShadockSyllable syllable = ShadockSyllable.None) => syllable.ToString());

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["say", "--BU", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("Unknown option '--BU'");
	}

	[TestMethod]
	[Description("Guards the unset state of the Arity override: an attributed collection parameter without an explicit Arity must keep the inferred ZeroOrMore, so a builder-site drift back to the public (non-nullable) property — which compiles cleanly under ?. — would wrongly force ZeroOrOne and break repetition.")]
	public void When_AttributedCollectionParameterWithoutArityOverride_Then_RepetitionIsAccepted()
	{
		var sut = ReplApp.Create();
		sut.Map("echo", ([ReplOption(Name = "item")] string[] items) => string.Join(',', items));

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["echo", "--item", "ga", "--item", "bu", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("ga,bu");
	}

	[TestMethod]
	[Description("Guards the unset state of the CaseSensitivity override at execution: an attributed option without an explicit override must inherit the global CaseInsensitive default instead of the enum default (CaseSensitive, value 0).")]
	public void When_GlobalCaseInsensitiveAndNoOverride_Then_CasingVariantIsAccepted()
	{
		var sut = ReplApp.Create()
			.Options(options => options.Parsing.OptionCaseSensitivity = ReplCaseSensitivity.CaseInsensitive);
		sut.Map("echo", ([ReplOption(Name = "channel")] string channel) => channel);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["echo", "--Channel", "zo", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("zo");
	}

	[TestMethod]
	[Description("Guards the explicit-assignment-of-enum-value-zero edge: [ReplOption(CaseSensitivity = CaseSensitive)] (enum value 0) under a global CaseInsensitive default must register as a real override and reject casing variants — a sentinel-style refactor treating value 0 as unset would silently pass them.")]
	public void When_ExplicitCaseSensitiveOverrideUnderGlobalInsensitive_Then_CasingVariantIsRejected()
	{
		var sut = ReplApp.Create()
			.Options(options => options.Parsing.OptionCaseSensitivity = ReplCaseSensitivity.CaseInsensitive);
		sut.Map("echo", ([ReplOption(CaseSensitivity = ReplCaseSensitivity.CaseSensitive)] string channel) => channel);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["echo", "--Channel", "zo", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("Unknown option '--Channel'");
	}

	[TestMethod]
	[Description("Guards the options-group property path (a parallel branch in OptionSchemaBuilder): a case-sensitivity override on a group property must be honored the same way as on a handler parameter.")]
	public void When_GroupPropertyCaseSensitivityOverridden_Then_CasingVariantIsAccepted()
	{
		var sut = ReplApp.Create();
		sut.Map("wear", (DenimOutfitOptions options) => options.Fabric);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["wear", "--FABRIC", "bib overalls", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("bib overalls");
	}

	[TestMethod]
	[Description("Guards the options-group property path for the arity override: a collection group property naturally infers ZeroOrMore; the explicit ZeroOrOne override must be honored so repetition is rejected.")]
	public void When_GroupPropertyArityOverriddenToZeroOrOne_Then_RepeatedOptionIsRejected()
	{
		var sut = ReplApp.Create();
		sut.Map("wear", (DenimOutfitOptions options) => string.Join(',', options.Patches));

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["wear", "--patches", "ga", "--patches", "bu", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("accepts at most one value");
	}

	[TestMethod]
	[Description("Guards execution parity for enum duplicate detection: with an explicit ZeroOrMore arity (reachable only through the now-settable override), repeated enum values differing only by casing are distinct under the global CaseSensitive default and must be reported as conflicting — the validator previously hardcoded case-insensitive comparison when no per-option override was set.")]
	public void When_RepeatedEnumValuesDifferByCaseUnderGlobalCaseSensitive_Then_ConflictIsReported()
	{
		var sut = ReplApp.Create();
		sut.Map("say", ([ReplOption(Arity = ReplArity.ZeroOrMore)] ShadockSyllable syllable = ShadockSyllable.None) => syllable.ToString());

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["say", "--syllable", "Ga", "--syllable", "GA", "--no-logo"]));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("received multiple enum values");
	}

	[TestMethod]
	[Description("Guards against a silent no-op newly reachable through issue #57: typed global options (UseGlobalOptions<T>) do not support per-option CaseSensitivity/Arity overrides, so declaring one must fail fast at registration instead of being silently discarded.")]
	public void When_GlobalOptionsPropertyDeclaresOverride_Then_RegistrationFailsFast()
	{
		var act = () => ReplApp.Create().UseGlobalOptions<OverrideGlobals>();

		act.Should().Throw<NotSupportedException>().WithMessage("*CaseSensitivity*");
	}
}
