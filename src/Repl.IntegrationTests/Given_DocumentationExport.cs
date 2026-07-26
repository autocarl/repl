namespace Repl.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class Given_DocumentationExport
{
	[TestMethod]
	[Description("Regression guard: verifies documentation export is enabled so that aggregate export excludes hidden commands.")]
	public void When_ExportingAggregateDocumentation_Then_HiddenCommandsAreExcluded()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map("contact list", () => "ok");
		sut.Map("contact debug dump-state", () => "hidden").Hidden();

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"path\": \"contact list\"");
		output.Text.Should().NotContain("contact debug dump-state");
	}

	[TestMethod]
	[Description("Regression guard: verifies aggregate documentation export excludes hidden contexts and their command trees.")]
	public void When_ExportingAggregateDocumentation_Then_HiddenContextsAreExcluded()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Context("admin", admin =>
		{
			admin.Map("reset", () => "done");
		}).Hidden();
		sut.Map("status", () => "ok");

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"path\": \"status\"");
		output.Text.Should().NotContain("\"path\": \"admin\"");
		output.Text.Should().NotContain("admin reset");
	}

	[TestMethod]
	[Description("Regression guard: verifies hidden command is explicitly targeted so that exact-path export includes hidden node.")]
	public void When_ExportingExactHiddenCommand_Then_HiddenCommandIsIncluded()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map("contact list", () => "ok");
		sut.Map("contact debug dump-state", () => "hidden").Hidden();

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "contact", "debug", "dump-state", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"path\": \"contact debug dump-state\"");
		output.Text.Should().Contain("\"isHidden\": true");
	}

	[TestMethod]
	[Description("Mirrors the command axis one level down: an explicitly targeted command exports its hidden options, flagged, exactly as a targeted hidden command exports itself. Aggregate export still omits them, which is what keeps them out of the MCP tool schema and its argument allow-list. This targeted export is the only surface an app author has for asking which options are hidden.")]
	public void When_ExportingExactCommandWithHiddenOption_Then_TheOptionIsIncludedAndFlagged()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map(
				"deploy",
				([ReplOption(Name = "environment")] string environment, [ReplOption(Name = "internalMode")] bool internalMode = false) =>
					$"{environment}:{internalMode}")
			.WithOption("internalMode", static option => option.Hidden());

		var aggregate = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "--json", "--no-logo"]));
		var targeted = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--json", "--no-logo"]));

		aggregate.ExitCode.Should().Be(0, aggregate.Text);
		aggregate.Text.Should().NotContain("internalMode");
		targeted.ExitCode.Should().Be(0, targeted.Text);
		targeted.Text.Should().Contain("internalMode");
		targeted.Text.Should().Contain("\"isHidden\": true");
	}

	[TestMethod]
	[Description("The exact-target export contract promises hidden options are included AND flagged. The structured formats get that for free by serializing the record, but markdown formats each field by hand, so without this it rendered a hidden option indistinguishably from a public one. Commands already print their own Hidden line; options now carry the same information.")]
	public void When_ExportingExactCommandAsMarkdown_Then_HiddenOptionsAreFlagged()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map(
				"deploy",
				([ReplOption(Name = "environment")] string environment,
					[ReplOption(Name = "internalMode")] bool internalMode = false,
					[ReplOption(Name = "traceId")] string? traceId = null) =>
					$"{environment}:{internalMode}:{traceId}")
			.WithOption("internalMode", static option => option.Hidden())
			.WithOption("traceId", static option => option.AutomationHidden());

		var markdown = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--markdown", "--no-logo"]));

		markdown.ExitCode.Should().Be(0, markdown.Text);
		markdown.Text.Should().Contain("`--internalMode`");
		markdown.Text.Should().Contain("hidden");
		markdown.Text.Should().Contain("`--traceId`");
		markdown.Text.Should().Contain("automation-hidden");
		markdown.Text.Should().Contain("`--environment`");
	}

	[TestMethod]
	[Description("Markdown renders the surviving invocable reverse alias when global precedence removes the ordinary route token, rather than inventing the unreachable canonical spelling.")]
	public void When_OnlyReverseAliasRemainsReachable_Then_MarkdownRendersThatAlias()
	{
		var sut = ReplApp.Create().UseDocumentationExport();
		sut.Options(options =>
		{
			options.Parsing.AddGlobalOption<bool>("force");
			options.Parsing.GlobalOption("force").Hidden();
		});
		sut.Map(
			"deploy",
			static string ([ReplOption(ReverseAliases = ["--no-force"])] bool force = true) => force.ToString());

		var markdown = ConsoleCaptureHelper.Capture(() =>
			sut.Run(["doc", "export", "deploy", "--markdown", "--no-logo"]));

		markdown.ExitCode.Should().Be(0, markdown.Text);
		markdown.Text.Should().Contain("`--no-force`");
		markdown.Text.Should().NotContain("`--force`");
	}

	[TestMethod]
	[Description("Exact-target inventory retains a wholly hidden route option even when a hidden global owns its only token; every export format identifies it and preserves the hidden flag.")]
	public void When_HiddenGlobalOwnsWhollyHiddenOptionToken_Then_ExactExportsRetainTheOption()
	{
		var sut = ReplApp.Create().UseDocumentationExport();
		sut.Options(options =>
		{
			options.Parsing.AddGlobalOption<bool>("internal-mode");
			options.Parsing.GlobalOption("internal-mode").Hidden();
		});
		sut.Map(
				"deploy",
				static string ([ReplOption(Name = "internal-mode")] bool internalMode = false) => internalMode.ToString())
			.WithOption("internalMode", static option => option.Hidden());

		var modelOption = sut.CreateDocumentationModel("deploy").Commands.Single().Options.Single();
		var json = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--json", "--no-logo"]));
		var yaml = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--yaml", "--no-logo"]));
		var xml = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--xml", "--no-logo"]));
		var markdown = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--markdown", "--no-logo"]));

		modelOption.Name.Should().Be("internal-mode");
		modelOption.IsHidden.Should().BeTrue();
		foreach (var export in new[] { json, yaml, xml, markdown })
		{
			export.ExitCode.Should().Be(0, export.Text);
			export.Text.Should().Contain("internal-mode");
			export.Text.Should().ContainEquivalentOf("hidden");
		}
	}

	[TestMethod]
	[Description("The visibility flags are new members on a serialized public record, and yaml and xml emit that record wholesale rather than field by field. Only json and markdown had coverage, so this exercises the two formats that would have broken silently.")]
	public void When_ExportingExactCommandAsYamlOrXml_Then_VisibilityFlagsSerialize()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map(
				"deploy",
				([ReplOption(Name = "environment")] string environment, [ReplOption(Name = "internalMode")] bool internalMode = false) =>
					$"{environment}:{internalMode}")
			.WithOption("internalMode", static option => option.Hidden());

		var yaml = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--yaml", "--no-logo"]));
		var xml = ConsoleCaptureHelper.Capture(() => sut.Run(["doc", "export", "deploy", "--xml", "--no-logo"]));

		yaml.ExitCode.Should().Be(0, yaml.Text);
		yaml.Text.Should().Contain("internalMode");
		xml.ExitCode.Should().Be(0, xml.Text);
		xml.Text.Should().Contain("internalMode");
	}

	[TestMethod]
	[Description("Regression guard: verifies hidden context is explicitly targeted so exact-path export includes the hidden context metadata.")]
	public void When_ExportingExactHiddenContext_Then_HiddenContextIsIncluded()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Context("admin", admin =>
		{
			admin.Map("reset", () => "done");
		}).Hidden();

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "admin", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"path\": \"admin\"");
		output.Text.Should().Contain("\"isHidden\": true");
		output.Text.Should().Contain("\"path\": \"admin reset\"");
	}

	[TestMethod]
	[Description("Regression guard: verifies markdown output is requested so that documentation export renders markdown.")]
	public void When_ExportingDocumentationInMarkdown_Then_MarkdownPayloadIsRendered()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map("contact list", () => "ok");

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "--markdown", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("# Overview");
		output.Text.Should().Contain("## Commands");
		output.Text.Should().Contain("`contact list`");
	}

	[TestMethod]
	[Description("Regression guard: verifies documentation export command is hidden by default so that help output does not advertise it.")]
	public void When_RequestingRootHelp_Then_DocumentationExportCommandIsHidden()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map("contact list", () => "ok");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["--help", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("contact list");
		output.Text.Should().NotContain("doc export");
	}

	[TestMethod]
	[Description("Regression guard: verifies temporal route constraints are exported so that documentation surfaces typed route arguments.")]
	public void When_ExportingDocumentationAsJson_Then_TemporalConstraintTypesAreIncluded()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map("report {day:date} {duration:timespan}", (DateOnly day, TimeSpan duration) => $"{day}:{duration}");

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"path\": \"report {day:date} {duration:timespan}\"");
		output.Text.Should().Contain("\"type\": \"date\"");
		output.Text.Should().Contain("\"type\": \"timespan\"");
	}

	[TestMethod]
	[Description("Regression guard: verifies option metadata export includes aliases, reverse aliases, enum values, and defaults so external tooling can reconstruct invocation UX.")]
	public void When_ExportingDocumentationAsJson_Then_OptionMetadataIncludesSchemaDetails()
	{
		var sut = ReplApp.Create()
			.UseDocumentationExport();
		sut.Map(
			"render",
			([ReplOption(Aliases = ["-m"])] ExportMode mode = ExportMode.Fast,
				[ReplOption(ReverseAliases = ["--no-verbose"])] bool verbose = false) => $"{mode}:{verbose}");

		var output = ConsoleCaptureHelper.Capture(
			() => sut.Run(["doc", "export", "--json", "--no-logo"]));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("\"aliases\": [");
		output.Text.Should().Contain("\"-m\"");
		output.Text.Should().Contain("\"reverseAliases\": [");
		output.Text.Should().Contain("\"--no-verbose\"");
		output.Text.Should().Contain("\"enumValues\": [");
		output.Text.Should().Contain("\"Fast\"");
		output.Text.Should().Contain("\"Slow\"");
		output.Text.Should().Contain("\"defaultValue\": \"Fast\"");
	}

	private enum ExportMode
	{
		Fast,
		Slow,
	}
}
