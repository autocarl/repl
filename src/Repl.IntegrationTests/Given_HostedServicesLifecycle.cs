using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Repl.IntegrationTests;

[TestClass]
[DoNotParallelize]
public sealed class Given_HostedServicesLifecycle
{
	[TestMethod]
	[Description("Regression guard: verifies default run options do not orchestrate hosted services so that external hosts can manage lifecycle.")]
	public void When_RunningWithDefaultLifecycleMode_Then_HostedServicesAreNotStartedOrStopped()
	{
		var tracker = new LifecycleTracker();
		var services = new ServiceCollection()
			.AddSingleton(tracker)
			.AddSingleton<IHostedService, TrackingHostedService>();
		using var provider = services.BuildServiceProvider();

		var sut = ReplApp.Create();
		sut.Map("status", (LifecycleTracker state) => $"{state.StartCount}/{state.StopCount}");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(["status", "--no-logo"], provider));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("0/0");
	}

	[TestMethod]
	[Description("Regression guard: verifies internal-provider run with none lifecycle mode does not orchestrate hosted services.")]
	public void When_RunningWithInternalProviderAndNoneMode_Then_HostedServicesAreNotStartedOrStopped()
	{
		var sut = ReplApp.Create(services =>
		{
			services.AddSingleton<LifecycleTracker>();
			services.AddSingleton<IHostedService, TrackingHostedService>();
		});
		sut.Map("status", (LifecycleTracker state) => $"{state.StartCount}/{state.StopCount}");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.None }));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("0/0");
	}

	[TestMethod]
	[Description("Regression guard: verifies host overload with none lifecycle mode does not orchestrate hosted services.")]
	public void When_RunningWithHostAndNoneMode_Then_HostedServicesAreNotStartedOrStopped()
	{
		using var host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<LifecycleTracker>();
				services.AddSingleton<IHostedService, TrackingHostedService>();
			})
			.Build();
		var tracker = host.Services.GetRequiredService<LifecycleTracker>();

		var sut = ReplApp.Create();
		sut.Map("status", (LifecycleTracker state) => $"{state.StartCount}/{state.StopCount}");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			host,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.None }));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("0/0");
		tracker.StartCount.Should().Be(0);
		tracker.StopCount.Should().Be(0);
	}

	[TestMethod]
	[Description("Regression guard: verifies head lifecycle mode orchestrates hosted services so that start and stop are invoked around execution.")]
	public void When_RunningWithHeadLifecycleMode_Then_HostedServicesAreStartedAndStopped()
	{
		var tracker = new LifecycleTracker();
		var services = new ServiceCollection()
			.AddSingleton(tracker)
			.AddSingleton<IHostedService, TrackingHostedService>();
		using var provider = services.BuildServiceProvider();

		var sut = ReplApp.Create();
		sut.Map("status", (LifecycleTracker state) => state.StartCount);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("1");
		tracker.StartCount.Should().Be(1);
		tracker.StopCount.Should().Be(1);
	}

	[TestMethod]
	[Description("Regression guard: verifies internal-provider run with head lifecycle mode orchestrates hosted services around execution.")]
	public void When_RunningWithInternalProviderAndHeadMode_Then_HostedServicesAreStartedAndStopped()
	{
		var sut = ReplApp.Create(services =>
		{
			services.AddSingleton<LifecycleTracker>();
			services.AddSingleton<IHostedService, TrackingHostedService>();
		});
		sut.Map("status", (LifecycleTracker state) => state.StartCount);

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("1");
	}

	[TestMethod]
	[Description("Regression guard: verifies host overload with head lifecycle mode orchestrates hosted services around execution.")]
	public void When_RunningWithHostAndHeadMode_Then_HostedServicesAreStartedAndStopped()
	{
		using var host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<LifecycleTracker>();
				services.AddSingleton<IHostedService, TrackingHostedService>();
			})
			.Build();
		var tracker = host.Services.GetRequiredService<LifecycleTracker>();

		var sut = ReplApp.Create();
		sut.Map("status", (LifecycleTracker state) => $"{state.StartCount}/{state.StopCount}");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			host,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("1/0");
		tracker.StartCount.Should().Be(1);
		tracker.StopCount.Should().Be(1);
	}

	[TestMethod]
	[Description("Regression guard: verifies async host overload with head lifecycle mode orchestrates hosted services around execution.")]
	public async Task When_RunningAsyncWithHostAndHeadMode_Then_HostedServicesAreStartedAndStopped()
	{
		using var host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<LifecycleTracker>();
				services.AddSingleton<IHostedService, TrackingHostedService>();
			})
			.Build();
		var tracker = host.Services.GetRequiredService<LifecycleTracker>();

		var sut = ReplApp.Create();
		sut.Map("status", (LifecycleTracker state) => $"{state.StartCount}/{state.StopCount}");

		var output = await ConsoleCaptureHelper.CaptureAsync(() => sut.RunAsync(
			["status", "--no-logo"],
			host,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head })
			.AsTask());

		output.ExitCode.Should().Be(0);
		output.Text.Should().Contain("1/0");
		tracker.StartCount.Should().Be(1);
		tracker.StopCount.Should().Be(1);
	}

	[TestMethod]
	[Description("Regression guard: verifies hosted service startup failures surface as execution errors so that lifecycle startup is observable.")]
	public void When_HeadLifecycleStartupFails_Then_ExecutionFailsWithError()
	{
		var services = new ServiceCollection()
			.AddSingleton<IHostedService, StartFailingHostedService>();
		using var provider = services.BuildServiceProvider();

		var sut = ReplApp.Create();
		sut.Map("status", () => "ok");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("Failed to start hosted service");
	}

	[TestMethod]
	[Description("Regression guard: verifies hosted service stop failures turn run into an error so that lifecycle teardown problems are not silent.")]
	public void When_HeadLifecycleStopFails_Then_ExecutionFailsWithError()
	{
		var services = new ServiceCollection()
			.AddSingleton<IHostedService, StopFailingHostedService>();
		using var provider = services.BuildServiceProvider();

		var sut = ReplApp.Create();
		sut.Map("status", () => "ok");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(1);
		output.Text.Should().Contain("Failed to stop hosted service");
	}

	[TestMethod]
	[Description("Regression guard: verifies a hosting failure goes through the exit-code policy so that an application can publish its own code for a run that could not host its services.")]
	public void When_HeadLifecycleStopFailsAndFrameworkErrorIsRemapped_Then_ConfiguredCodeIsReturned()
	{
		var services = new ServiceCollection()
			.AddSingleton<IHostedService, StopFailingHostedService>();
		using var provider = services.BuildServiceProvider();

		var sut = ReplApp.Create();
		sut.Options(options => options.ExitCodes.FrameworkError = 70);
		sut.Map("status", () => "ok");

		var output = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		output.ExitCode.Should().Be(70);
	}

	[TestMethod]
	[Description("Regression guard: verifies a run reports exactly one outcome even when hosted-service shutdown fails, so a resolver with side effects is never handed a second outcome for the same run.")]
	public void When_HeadLifecycleStopFails_Then_TheResolverObservesASingleOutcome()
	{
		var services = new ServiceCollection()
			.AddSingleton<IHostedService, StopFailingHostedService>();
		using var provider = services.BuildServiceProvider();

		var observed = new List<ReplExecutionOutcomeKind>();
		var sut = ReplApp.Create();
		sut.Options(options => options.ExitCodes.Resolver = outcome =>
		{
			observed.Add(outcome.Kind);
			return outcome.ExitCode;
		});
		sut.Map("status", () => "ok");

		_ = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		observed.Should().Equal(ReplExecutionOutcomeKind.FrameworkError);
	}

	[TestMethod]
	[Description("Regression guard: verifies an already-cancelled token in head lifecycle mode follows ExitCodes.Cancelled without starting hosted services, so the policy is not bypassed by the hosting wrapper.")]
	public async Task When_HeadLifecycleTokenIsPreCancelledAndCancelledIsMapped_Then_CodeIsReturnedWithoutStartingServices()
	{
		var services = new ServiceCollection()
			.AddSingleton<LifecycleTracker>()
			.AddSingleton<IHostedService, TrackingHostedService>();
		using var provider = services.BuildServiceProvider();
		var tracker = provider.GetRequiredService<LifecycleTracker>();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var sut = ReplApp.Create();
		sut.Options(options => options.ExitCodes.Cancelled = 130);
		sut.Map("status", () => "ok");

		var exitCode = await sut.RunAsync(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head },
			cts.Token);

		exitCode.Should().Be(130);
		tracker.StartCount.Should().Be(0);
		tracker.StopCount.Should().Be(0);
	}

	[TestMethod]
	[Description("Regression guard: verifies an already-cancelled token still throws in head lifecycle mode when no cancellation policy is configured, preserving the existing caller contract.")]
	public async Task When_HeadLifecycleTokenIsPreCancelledAndNoPolicyIsSet_Then_OperationCanceledExceptionPropagates()
	{
		var services = new ServiceCollection()
			.AddSingleton<LifecycleTracker>()
			.AddSingleton<IHostedService, TrackingHostedService>();
		using var provider = services.BuildServiceProvider();
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();

		var sut = ReplApp.Create();
		sut.Map("status", () => "ok");

		Func<Task> act = () => sut.RunAsync(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head },
			cts.Token).AsTask();

		await act.Should().ThrowAsync<OperationCanceledException>();
	}

	[TestMethod]
	[Description("Regression guard: verifies a hosting failure hands the lifecycle exception to the resolver, since the coordinator wraps whatever the service threw and that is the only way a consumer can inspect it.")]
	public void When_HeadLifecycleStopFails_Then_TheOutcomeCarriesTheLifecycleException()
	{
		var services = new ServiceCollection()
			.AddSingleton<IHostedService, StopFailingHostedService>();
		using var provider = services.BuildServiceProvider();

		ReplExecutionOutcome? observed = null;
		var sut = ReplApp.Create();
		sut.Options(options => options.ExitCodes.Resolver = outcome =>
		{
			observed = outcome;
			return outcome.ExitCode;
		});
		sut.Map("status", () => "ok");

		_ = ConsoleCaptureHelper.Capture(() => sut.Run(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head }));

		observed!.Kind.Should().Be(ReplExecutionOutcomeKind.FrameworkError);
		observed.Exception.Should().NotBeNull();
		observed.Exception!.Message.Should().Contain("Failed to stop hosted service");
	}

	[TestMethod]
	[Description("Regression guard: verifies a startup cancelled through the caller's token is a Cancelled outcome, not a framework error: the coordinator wraps the OperationCanceledException, which used to hide it from ExitCodes.Cancelled.")]
	public async Task When_HeadLifecycleStartupIsCancelledByCaller_Then_KindIsCancelledAndCancelledCodeApplies()
	{
		using var cts = new CancellationTokenSource();
		var services = new ServiceCollection()
			.AddSingleton<CancellationTokenSource>(cts)
			.AddSingleton<IHostedService, CallerCancellingHostedService>();
		using var provider = services.BuildServiceProvider();

		ReplExecutionOutcome? observed = null;
		var sut = ReplApp.Create();
		sut.Options(options =>
		{
			options.ExitCodes.Cancelled = 130;
			options.ExitCodes.Resolver = outcome =>
			{
				observed = outcome;
				return outcome.ExitCode;
			};
		});
		sut.Map("status", () => "ok");

		var exitCode = await sut.RunAsync(
			["status", "--no-logo"],
			provider,
			new ReplRunOptions { HostedServiceLifecycle = HostedServiceLifecycleMode.Head },
			cts.Token);

		exitCode.Should().Be(130);
		observed!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
		observed.Exception.Should().NotBeNull();
	}

	// Cancels the caller's token from inside StartAsync, then observes it: the shape that reaches
	// ReplApp as a HostedServiceLifecycleException wrapping an OperationCanceledException.
	private sealed class CallerCancellingHostedService(CancellationTokenSource callerTokenSource) : IHostedService
	{
		public async Task StartAsync(CancellationToken cancellationToken)
		{
			await callerTokenSource.CancelAsync().ConfigureAwait(false);
			cancellationToken.ThrowIfCancellationRequested();
		}

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class LifecycleTracker
	{
		public int StartCount { get; private set; }

		public int StopCount { get; private set; }

		public void OnStarted() => StartCount++;

		public void OnStopped() => StopCount++;
	}

	private sealed class TrackingHostedService(LifecycleTracker tracker) : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken)
		{
			tracker.OnStarted();
			return Task.CompletedTask;
		}

		public Task StopAsync(CancellationToken cancellationToken)
		{
			tracker.OnStopped();
			return Task.CompletedTask;
		}
	}

	private sealed class StartFailingHostedService : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken) =>
			Task.FromException(new InvalidOperationException("boom-start"));

		public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class StopFailingHostedService : IHostedService
	{
		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task StopAsync(CancellationToken cancellationToken) =>
			Task.FromException(new InvalidOperationException("boom-stop"));
	}
}
