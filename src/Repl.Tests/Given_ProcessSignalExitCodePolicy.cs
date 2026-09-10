using AwesomeAssertions;

namespace Repl.Tests;

/// <summary>
/// A claimed process signal reports its interruption through the exit-code policy rather than
/// overwriting an already-resolved number, so <c>ExitCodes.Interrupted</c> and
/// <c>ExitCodes.Resolver</c> both govern a signalled run.
/// </summary>
[TestClass]
public sealed class Given_ProcessSignalExitCodePolicy
{
	[TestMethod]
	[Description("Regression guard: verifies a claimed process signal resolves through ExitCodes.Interrupted. The signal used to overwrite the run's already-resolved exit code afterwards, which left the table entry inert and handed a resolver two outcomes for one run.")]
	public async Task When_ASignalIsClaimed_Then_InterruptedGovernsTheExitCode()
	{
		using var isolation = ProcessSignalCoordinator.IsolateRegistrationsForTesting();
		ReplExecutionOutcome? observed = null;
		var sut = CreateSignalledApp(outcome => observed = outcome, options => options.ExitCodes.Interrupted = 75);

		var exitCode = await RunWithSignalAsync(sut).ConfigureAwait(false);

		exitCode.Should().Be(75);
		observed!.Kind.Should().Be(ReplExecutionOutcomeKind.Interrupted);
	}

	[TestMethod]
	[Description("Regression guard: verifies an unmapped interruption keeps the conventional 128+signal code the signal carries, so wiring the policy did not change the published default for a Ctrl+C run.")]
	public async Task When_InterruptedIsUnmapped_Then_TheConventionalCodeIsKept()
	{
		using var isolation = ProcessSignalCoordinator.IsolateRegistrationsForTesting();
		ReplExecutionOutcome? observed = null;
		var sut = CreateSignalledApp(outcome => observed = outcome);

		var exitCode = await RunWithSignalAsync(sut).ConfigureAwait(false);

		exitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		observed!.Kind.Should().Be(ReplExecutionOutcomeKind.Interrupted);
	}

	[TestMethod]
	[Description("Regression guard: verifies a resolver sees exactly one outcome for a signalled run. The number-overwriting form resolved the run once and then replaced its code, so a hook with side effects observed an outcome that was not the one reported.")]
	public async Task When_ASignalIsClaimed_Then_TheResolverObservesOneOutcome()
	{
		using var isolation = ProcessSignalCoordinator.IsolateRegistrationsForTesting();
		var observed = new List<ReplExecutionOutcomeKind>();
		var sut = CreateSignalledApp(outcome => observed.Add(outcome.Kind));

		_ = await RunWithSignalAsync(sut).ConfigureAwait(false);

		observed.Should().Equal(ReplExecutionOutcomeKind.Interrupted);
	}

	[TestMethod]
	[Description("Regression guard: verifies a run that produced its own refusal keeps reporting it when a signal lands, rather than having a usage error replaced by the interruption code, which would hide why the command was wrong.")]
	public void When_TheRunAlreadyFailed_Then_ItKeepsItsOwnOutcome()
	{
		// The precedence rule itself, on the pure predicate that carries it. Asserted per kind rather
		// than through a race between a signal and a failing command, which no test could sequence.
		ExecutionOutcome.Success.IsInterruptible.Should().BeTrue();
		ExecutionOutcome.Help.IsInterruptible.Should().BeTrue();
		ExecutionOutcome.Cancelled(new OperationCanceledException()).IsInterruptible.Should().BeTrue();

		ExecutionOutcome.UsageError().IsInterruptible.Should().BeFalse();
		ExecutionOutcome.HandlerError(Results.Error("boom", "failed")).IsInterruptible.Should().BeFalse();
		ExecutionOutcome.HandlerException(new FormatException("boom")).IsInterruptible.Should().BeFalse();
		ExecutionOutcome.BindingError(new FormatException("boom")).IsInterruptible.Should().BeFalse();
		ExecutionOutcome.FrameworkError(rendered: null).IsInterruptible.Should().BeFalse();
	}

	private static ReplApp CreateSignalledApp(
		Action<ReplExecutionOutcome> observe,
		Action<ReplOptions>? configure = null)
	{
		var app = ReplApp.Create();
		app.Options(options =>
		{
			options.Output.BannerEnabled = false;
			options.Interactive.InteractivePolicy = InteractivePolicy.Prevent;
			configure?.Invoke(options);
			options.ExitCodes.Resolver = outcome =>
			{
				observe(outcome);
				return outcome.ExitCode;
			};
		});

		// Raises the signal from inside the handler, which is the only in-process way to have one
		// claimed while a run is genuinely in flight.
		app.Map("work", (CancellationToken ct) =>
		{
			_ = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
			ct.ThrowIfCancellationRequested();
			return "unreachable";
		});
		return app;
	}

	private static async ValueTask<int> RunWithSignalAsync(ReplApp sut)
	{
		using var writer = new StringWriter();
		using var session = ReplSessionIO.SetSession(writer, TextReader.Null, commandOutput: writer, error: writer);
		return await sut.RunAsync(
				["work"],
				new ReplRunOptions { ProcessSignalHandling = ProcessSignalHandlingMode.Automatic })
			.ConfigureAwait(false);
	}
}
