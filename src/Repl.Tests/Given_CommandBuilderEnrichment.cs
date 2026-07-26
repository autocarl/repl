using Repl.Documentation;

namespace Repl.Tests;

[TestClass]
public sealed class Given_CommandBuilderEnrichment
{
	// ── WithDetails ────────────────────────────────────────────────────

	[TestMethod]
	[Description("Verifies WithDetails stores markdown content.")]
	public void When_WithDetailsIsCalled_Then_DetailsIsStored()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		command.WithDetails("Deploys the application.");

		command.Details.Should().Be("Deploys the application.");
	}

	[TestMethod]
	[Description("Verifies WithDetails rejects empty content.")]
	public void When_WithDetailsIsCalledWithEmpty_Then_Throws()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		var act = () => command.WithDetails("   ");

		act.Should().Throw<ArgumentException>();
	}

	// ── Annotation shortcuts ───────────────────────────────────────────

	[TestMethod]
	[Description("Verifies ReadOnly shortcut creates and sets annotation.")]
	public void When_ReadOnlyIsCalled_Then_AnnotationIsSet()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("list", () => "ok");

		var chained = command.ReadOnly();

		chained.Should().BeSameAs(command);
		command.Annotations.Should().NotBeNull();
		command.Annotations!.ReadOnly.Should().BeTrue();
	}

	[TestMethod]
	[Description("Verifies chaining multiple annotation shortcuts preserves all flags.")]
	public void When_MultipleShortsAreChained_Then_AllFlagsArePreserved()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		command.Destructive().LongRunning().OpenWorld();

		command.Annotations!.Destructive.Should().BeTrue();
		command.Annotations!.LongRunning.Should().BeTrue();
		command.Annotations!.OpenWorld.Should().BeTrue();
		command.Annotations!.ReadOnly.Should().BeFalse();
	}

	[TestMethod]
	[Description("Verifies WithAnnotations builder escape hatch works and overwrites shortcuts.")]
	public void When_WithAnnotationsIsCalled_Then_PreviousShortcutsAreOverwritten()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		command.ReadOnly();
		command.WithAnnotations(a => a.Destructive().OpenWorld());

		command.Annotations!.ReadOnly.Should().BeFalse();
		command.Annotations!.Destructive.Should().BeTrue();
		command.Annotations!.OpenWorld.Should().BeTrue();
	}

	[TestMethod]
	[Description("Verifies WithAnnotations rejects null configure.")]
	public void When_WithAnnotationsIsCalledWithNull_Then_Throws()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		var act = () => command.WithAnnotations(null!);

		act.Should().Throw<ArgumentNullException>();
	}

	// ── AsResource / AsPrompt ──────────────────────────────────────────

	[TestMethod]
	[Description("Verifies AsResource marks the command as a resource.")]
	public void When_AsResourceIsCalled_Then_IsResourceIsTrue()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("contacts", () => "ok");

		command.AsResource();

		command.IsResource.Should().BeTrue();
	}

	[TestMethod]
	[Description("Verifies AsPrompt marks the command as a prompt source.")]
	public void When_AsPromptIsCalled_Then_IsPromptIsTrue()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("explain", () => "ok");

		command.AsPrompt();

		command.IsPrompt.Should().BeTrue();
	}

	// ── WithMetadata ───────────────────────────────────────────────────

	[TestMethod]
	[Description("Verifies WithMetadata stores key-value pairs.")]
	public void When_WithMetadataIsCalled_Then_EntryIsStored()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		command.WithMetadata("category", "operations");

		command.Metadata.Should().ContainKey("category");
		command.Metadata["category"].Should().Be("operations");
	}

	[TestMethod]
	[Description("Verifies WithMetadata rejects empty key.")]
	public void When_WithMetadataIsCalledWithEmptyKey_Then_Throws()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", () => "ok");

		var act = () => command.WithMetadata("", "value");

		act.Should().Throw<ArgumentException>();
	}

	// ── Documentation model enrichment ─────────────────────────────────

	[TestMethod]
	[Description("Verifies enriched fields propagate through the documentation model.")]
	public void When_DocumentationModelIsCreated_Then_EnrichedFieldsArePresent()
	{
		var sut = CoreReplApp.Create();
		sut.Map("contacts", () => "ok")
			.WithDescription("List contacts")
			.WithDetails("Returns all contacts.")
			.ReadOnly()
			.AsResource()
			.WithMetadata("scope", "crm");

		var model = sut.CreateDocumentationModel();

		var cmd = model.Commands.Should().ContainSingle(c => c.Path == "contacts").Which;
		cmd.Details.Should().Be("Returns all contacts.");
		cmd.Annotations.Should().NotBeNull();
		cmd.Annotations!.ReadOnly.Should().BeTrue();
		cmd.IsResource.Should().BeTrue();
		cmd.Metadata.Should().NotBeNull();
		cmd.Metadata!["scope"].Should().Be("crm");
	}

	[TestMethod]
	[Description("Verifies resources collection is populated from AsResource commands.")]
	public void When_CommandIsMarkedAsResource_Then_ResourcesCollectionContainsIt()
	{
		var sut = CoreReplApp.Create();
		sut.Map("contacts", () => "ok")
			.WithDescription("List contacts")
			.AsResource();

		var model = sut.CreateDocumentationModel();

		model.Resources.Should().ContainSingle(r => r.Path == "contacts");
	}

	[TestMethod]
	[Description("Verifies ReadOnly commands are auto-promoted to resources.")]
	public void When_CommandIsReadOnly_Then_ResourcesCollectionContainsIt()
	{
		var sut = CoreReplApp.Create();
		sut.Map("status", () => "ok")
			.WithDescription("Show status")
			.ReadOnly();

		var model = sut.CreateDocumentationModel();

		model.Resources.Should().ContainSingle(r => r.Path == "status");
	}

	[TestMethod]
	[Description("Verifies AsPrompt flag propagates through the documentation model.")]
	public void When_CommandIsMarkedAsPrompt_Then_IsPromptIsTrue()
	{
		var sut = CoreReplApp.Create();
		sut.Map("explain {code}", (string code) => $"Explain {code}")
			.AsPrompt();

		var model = sut.CreateDocumentationModel();

		model.Commands.Should().ContainSingle(c => c.Path == "explain {code}").Which
			.IsPrompt.Should().BeTrue();
	}

	[TestMethod]
	[Description("Verifies route argument descriptions are picked up from [Description] attributes.")]
	public void When_HandlerHasDescriptionAttribute_Then_ArgumentDescriptionIsPopulated()
	{
		var sut = CoreReplApp.Create();
		sut.Map("contact {id:int}", ([System.ComponentModel.Description("Contact numeric id")] int id) => id);

		var model = sut.CreateDocumentationModel();

		var arg = model.Commands.Should().ContainSingle(c => c.Path == "contact {id:int}").Which
			.Arguments.Should().ContainSingle().Which;
		arg.Description.Should().Be("Contact numeric id");
	}

	[TestMethod]
	[Description("An option hidden through the fluent builder disappears from the aggregate documentation model — the model MCP builds — but survives, flagged, when its command is targeted explicitly. That mirrors how a hidden command behaves and gives an app author the only way to inventory hidden options.")]
	public void When_CommandOptionIsHiddenFluently_Then_OnlyTheAggregateModelOmitsIt()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
			"deploy",
			([ReplOption] string environment, [ReplOption] bool internalMode = false) => $"{environment}:{internalMode}")
			.WithOption("internalMode", static option => option.Hidden());

		var aggregate = sut.CreateDocumentationModel().Commands.Should().ContainSingle().Which;
		var targeted = sut.CreateDocumentationModel("deploy").Commands.Should().ContainSingle().Which;

		aggregate.Options.Should().ContainSingle(option => option.Name == "environment");
		aggregate.Options.Should().NotContain(option => option.Name == "internalMode");
		targeted.Options.Should().Contain(option => option.Name == "environment" && !option.IsHidden);
		targeted.Options.Should().Contain(option => option.Name == "internalMode" && option.IsHidden);
	}

	[TestMethod]
	[Description("Hiding an option after Map publishes a new option schema rather than mutating one, so this asserts the parsing contract survives the swap: the accepted tokens and the resolved arity must be byte-identical before and after, while only the discovery projection changes. Without this, a future change to the swap could silently narrow what the parser accepts.")]
	public void When_OptionIsHiddenFluently_Then_ParsingContractIsUnchanged()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map(
			"deploy",
			([ReplOption] string environment, [ReplOption] bool internalMode = false) => $"{environment}:{internalMode}");
		var before = command.OptionSchema;
		string[] tokensBefore = [.. before.KnownTokens];
		var arityBefore = before.ResolveParameterArity("internalMode");

		command.WithOption("internalMode", option => option.Hidden());

		var after = command.OptionSchema;
		after.Should().NotBeSameAs(before, "visibility is published as a new schema, never mutated in place");
		after.KnownTokens.Should().Equal(tokensBefore, "a hidden option stays fully parsable");
		after.ResolveParameterArity("internalMode").Should().Be(arityBefore);
		after.Entries.Should().BeSameAs(before.Entries, "entries carry the parsing contract and are reused verbatim");
		after.IsOptionHidden("internalMode").Should().BeTrue();
		after.DiscoverableParameters.Should().NotContain(parameter => parameter.Name == "internalMode");
		after.DiscoverableParameters.Should().Contain(parameter => parameter.Name == "environment");
	}

	[TestMethod]
	[Description("AutomationHidden is the programmatic-only axis: unlike Hidden it keeps the option in the documentation model, so human-facing exports and help still show it and only the MCP projection drops it. Mirrors CommandAnnotations.AutomationHidden one level down.")]
	public void When_CommandOptionIsAutomationHidden_Then_TheDocumentationModelKeepsItFlagged()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
			"deploy",
			([ReplOption] string environment, [ReplOption] bool internalMode = false) => $"{environment}:{internalMode}")
			.WithOption("internalMode", static option => option.AutomationHidden());

		var aggregate = sut.CreateDocumentationModel().Commands.Should().ContainSingle().Which;

		aggregate.Options.Should().Contain(option =>
			option.Name == "internalMode" && option.IsAutomationHidden && !option.IsHidden);
		aggregate.Options.Should().Contain(option =>
			option.Name == "environment" && !option.IsAutomationHidden);
	}

	[TestMethod]
	[Description("Fluent visibility wins over the attribute on both axes and in both directions, because the attribute seeds the schema at Map while a fluent call publishes a new one afterwards. Asserting a single direction would miss an inverted precedence.")]
	public void When_AttributeAndFluentVisibilityDisagree_Then_FluentWins()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map(
			"deploy",
			([ReplOption(Hidden = true)] bool attributeHidden = false,
				[ReplOption(AutomationHidden = true)] bool attributeAutomationHidden = false,
				[ReplOption] bool fluentHidden = false,
				[ReplOption] bool fluentAutomationHidden = false) =>
				$"{attributeHidden}{attributeAutomationHidden}{fluentHidden}{fluentAutomationHidden}");

		command.WithOption("attributeHidden", option => option.Hidden(isHidden: false));
		command.WithOption("attributeAutomationHidden", option => option.AutomationHidden(isAutomationHidden: false));
		command.WithOption("fluentHidden", option => option.Hidden());
		command.WithOption("fluentAutomationHidden", option => option.AutomationHidden());

		var schema = command.OptionSchema;
		schema.IsOptionHidden("attributeHidden").Should().BeFalse();
		schema.IsOptionAutomationHidden("attributeAutomationHidden").Should().BeFalse();
		schema.IsOptionHidden("fluentHidden").Should().BeTrue();
		schema.IsOptionAutomationHidden("fluentAutomationHidden").Should().BeTrue();
	}

	[TestMethod]
	[Description("Selecting an unknown command option target fails clearly instead of leaving the intended option visible.")]
	public void When_SelectingUnknownCommandOption_Then_ConfigurationThrows()
	{
		var sut = CoreReplApp.Create();
		var command = sut.Map("deploy", ([ReplOption] bool force) => force);

		var act = () => command.WithOption("missing", static option => option.Hidden());

		act.Should().Throw<KeyNotFoundException>()
			.WithMessage("*option target*missing*deploy*");
	}

	[TestMethod]
	[Description("The declarative form reaches the same documentation contract as the fluent one: omitted from the aggregate model, present and flagged when the command is targeted.")]
	public void When_CommandOptionHasHiddenAttribute_Then_OnlyTheAggregateModelOmitsIt()
	{
		var sut = CoreReplApp.Create();
		sut.Map(
			"deploy",
			([ReplOption] string environment, [ReplOption(Hidden = true)] bool internalMode = false) => $"{environment}:{internalMode}");

		var aggregate = sut.CreateDocumentationModel().Commands.Should().ContainSingle().Which;
		var targeted = sut.CreateDocumentationModel("deploy").Commands.Should().ContainSingle().Which;

		aggregate.Options.Should().ContainSingle(option => option.Name == "environment");
		aggregate.Options.Should().NotContain(option => option.Name == "internalMode");
		targeted.Options.Should().Contain(option => option.Name == "internalMode" && option.IsHidden);
	}

	[TestMethod]
	[Description("Verifies injected IGlobalOptionsAccessor parameters are omitted from documentation options.")]
	public void When_HandlerUsesGlobalOptionsAccessor_Then_DocumentationOmitsAccessorOption()
	{
		var sut = CoreReplApp.Create();
		sut.Options(options => options.Parsing.AddGlobalOption<string>("tenant"));
		sut.Map("show", (IGlobalOptionsAccessor globals) => globals.GetValue<string>("tenant") ?? "none");

		var model = sut.CreateDocumentationModel("show");

		model.Commands.Should().ContainSingle(c => c.Path == "show").Which
			.Options.Should().NotContain(option => option.Name == "globals");
	}

	// ── ReplRuntimeChannel.Programmatic ────────────────────────────────

	[TestMethod]
	[Description("Verifies Programmatic channel value exists.")]
	public void When_ProgrammaticChannel_Then_ValueIs3()
	{
		((int)ReplRuntimeChannel.Programmatic).Should().Be(3);
	}
}
