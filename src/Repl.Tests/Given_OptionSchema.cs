using Repl.Internal.Options;

namespace Repl.Tests;

[TestClass]
public sealed class Given_OptionSchema
{
	[TestMethod]
	[Description("Given two parser-equivalent aliases under case-insensitive mode where the first needs to change and the second is already in the requested state, WithAliasVisibility must not let the second entry's no-op reset the accumulated 'changed' flag: the returned schema must reflect the first entry's real change instead of being discarded as a no-op.")]
	public void When_AnEarlierEquivalentAliasChangesButALaterOneAlreadyMatches_Then_TheChangeIsNotLost()
	{
		var caseSensitivity = ReplCaseSensitivity.CaseSensitive;
		var schema = new OptionSchema(
			[
				new OptionSchemaEntry("--tenant", "tenant", OptionSchemaTokenKind.NamedOption, ReplArity.ZeroOrOne),
				new OptionSchemaEntry("--ACCOUNT", "tenant", OptionSchemaTokenKind.NamedOption, ReplArity.ZeroOrOne, IsHidden: false),
				new OptionSchemaEntry("--account", "tenant", OptionSchemaTokenKind.NamedOption, ReplArity.ZeroOrOne, IsHidden: true),
			],
			new Dictionary<string, OptionSchemaParameter>(StringComparer.OrdinalIgnoreCase)
			{
				["tenant"] = new OptionSchemaParameter("tenant", typeof(string), ReplParameterMode.OptionOnly),
			},
			() => caseSensitivity);

		// Both --ACCOUNT and --account become equivalent to the requested alias "--ACCOUNT" once
		// case sensitivity turns insensitive, even though they were registered as distinct tokens.
		caseSensitivity = ReplCaseSensitivity.CaseInsensitive;
		var updated = schema.WithAliasVisibility("tenant", "--ACCOUNT", isHidden: true);

		caseSensitivity = ReplCaseSensitivity.CaseSensitive;
		updated.Entries.Should().ContainSingle(entry => entry.Token == "--ACCOUNT")
			.Which.IsHidden.Should().BeTrue("the first matching entry genuinely changed and must not be lost because a later equivalent entry was already hidden");
	}

	[TestMethod]
	[Description("An exact registered alias takes precedence over canonical-token equivalence when the active comparer is case-insensitive, so fluent visibility can restore that distinct alias spelling.")]
	public void When_ExactAliasMatchesCanonicalUnderCurrentComparer_Then_ExactAliasCanBeUnhidden()
	{
		var schema = new OptionSchema(
			[
				new OptionSchemaEntry("--tenant", "tenant", OptionSchemaTokenKind.NamedOption, ReplArity.ZeroOrOne),
				new OptionSchemaEntry("--TENANT", "tenant", OptionSchemaTokenKind.NamedOption, ReplArity.ZeroOrOne, IsHidden: true),
			],
			new Dictionary<string, OptionSchemaParameter>(StringComparer.OrdinalIgnoreCase)
			{
				["tenant"] = new OptionSchemaParameter("tenant", typeof(string), ReplParameterMode.OptionOnly),
			},
			ReplCaseSensitivity.CaseInsensitive);

		var updated = schema.WithAliasVisibility("tenant", "--TENANT", isHidden: false);

		updated.Entries.Should().ContainSingle(entry => entry.Token == "--TENANT")
			.Which.IsHidden.Should().BeFalse();
	}

}
