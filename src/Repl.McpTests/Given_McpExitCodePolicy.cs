using ModelContextProtocol.Protocol;

namespace Repl.McpTests;

[TestClass]
public sealed class Given_McpExitCodePolicy
{
	[TestMethod]
	[Description("Regression guard: verifies neither the exit-code table nor the resolver applies to MCP sub-invocations so that a process-level remap cannot hide a failed tool call from the agent.")]
	public async Task When_ExitCodePolicyMapsEverythingToZero_Then_ToolCallStillReportsError()
	{
		await using var fixture = await McpTestFixture.CreateAsync(app =>
		{
			app.Options(options =>
			{
				options.ExitCodes.HandlerError = 0;
				options.ExitCodes.Resolver = static _ => 0;
			});
			app.Map("boom", () => Results.Error("boom", "nope"))
				.ReadOnly();
		}).ConfigureAwait(false);

		var result = await fixture.Client.CallToolAsync(
			toolName: "boom",
			arguments: new Dictionary<string, object?>(StringComparer.Ordinal)).ConfigureAwait(false);

		result.IsError.Should().BeTrue();
		string.Join('\n', result.Content.OfType<TextContentBlock>().Select(static block => block.Text))
			.Should().Contain("nope");
	}
}
