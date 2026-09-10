using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

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

	[TestMethod]
	[DataRow(true, DisplayName = "with a cancellation policy configured")]
	[DataRow(false, DisplayName = "with no cancellation policy configured")]
	[Description("Regression guard: verifies a signal arriving while a hosted service is starting still reports the interruption. The coordinator wraps the cancellation in a HostedServiceLifecycleException, which used to be classified as a lifecycle failure and returned 1, and a non-zero code then defeated the signal's own.")]
	public async Task When_ASignalInterruptsHostedStartup_Then_TheInterruptionIsStillReported(bool mapCancelled)
	{
		using var isolation = ProcessSignalCoordinator.IsolateRegistrationsForTesting();
		ReplExecutionOutcome? observed = null;
		var app = ReplApp.Create(services =>
			services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService, SignallingHostedService>());
		app.Options(options =>
		{
			options.Output.BannerEnabled = false;
			options.Interactive.InteractivePolicy = InteractivePolicy.Prevent;
			if (mapCancelled)
			{
				options.ExitCodes.Cancelled = 66;
			}

			options.ExitCodes.Resolver = outcome =>
			{
				observed = outcome;
				return outcome.ExitCode;
			};
		});
		app.Map("work", () => "unreachable");

		using var writer = new StringWriter();
		using var session = ReplSessionIO.SetSession(writer, TextReader.Null, commandOutput: writer, error: writer);

		var exitCode = await app.RunAsync(
				["work"],
				new ReplRunOptions
				{
					ProcessSignalHandling = ProcessSignalHandlingMode.Automatic,
					HostedServiceLifecycle = HostedServiceLifecycleMode.Head,
				})
			.ConfigureAwait(false);

		// The signal decides, not the lifecycle wrapper and not ExitCodes.Cancelled: the run was
		// interrupted, so it reports the conventional signal code either way.
		observed!.Kind.Should().Be(ReplExecutionOutcomeKind.Interrupted);
		exitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
	}

	// Raises the signal from inside StartAsync, so the cancellation surfaces while the coordinator is
	// still starting services and gets wrapped in a HostedServiceLifecycleException.
	private sealed class SignallingHostedService : Microsoft.Extensions.Hosting.IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken)
		{
			_ = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
			cancellationToken.ThrowIfCancellationRequested();
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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
