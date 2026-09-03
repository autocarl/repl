using AwesomeAssertions;

namespace Repl.Tests;

[TestClass]
[DoNotParallelize]
public sealed class Given_ProcessSignalCancellationScope
{
	[TestMethod]
	[Description("Interactive CancelKeyHandler retains Ctrl+C ownership while a standalone signal scope surrounds the run.")]
	public async Task When_InteractiveCancelHandlerIsActive_Then_StandaloneScopeDoesNotClaimCtrlC()
	{
		await using var scope = new ProcessSignalCancellationScope(default);
		using var interactiveHandler = new CancelKeyHandler();

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();

		result.Should().Be(ConsoleCancelKeyHandlingResult.AllowProcessTermination);
		scope.ExitCode.Should().BeNull();
		scope.Token.IsCancellationRequested.Should().BeFalse();
	}

	[TestMethod]
	[Description("Ctrl+C is routed atomically to the active interactive handler instead of merely suppressing the standalone handler.")]
	public async Task When_InteractiveCancelHandlerIsActive_Then_CtrlCIsRoutedToIt()
	{
		await using var scope = new ProcessSignalCancellationScope(default);
		using var interactiveHandler = new CancelKeyHandler();
		using var commandCancellation = new CancellationTokenSource();
		interactiveHandler.SetCommandCts(commandCancellation);

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();

		result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		commandCancellation.IsCancellationRequested.Should().BeTrue();
		scope.ExitCode.Should().BeNull();
		scope.Token.IsCancellationRequested.Should().BeFalse();
	}

	[TestMethod]
	[Description("The first process signal cancels every standalone scope participating in the same ownership epoch.")]
	public async Task When_FirstCtrlCArrives_Then_AllActiveScopesAreCancelled()
	{
		await using var firstScope = new ProcessSignalCancellationScope(default);
		await using var secondScope = new ProcessSignalCancellationScope(default);

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();

		result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		firstScope.ExitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		secondScope.ExitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		firstScope.Token.IsCancellationRequested.Should().BeTrue();
		secondScope.Token.IsCancellationRequested.Should().BeTrue();
	}

	[TestMethod]
	[Description("Ctrl+Break follows the cooperative first-signal policy on Windows.")]
	public async Task When_FirstCtrlBreakArrivesOnWindows_Then_ActiveScopeIsCancelled()
	{
		await using var scope = new ProcessSignalCancellationScope(default);

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting(
			ConsoleSpecialKey.ControlBreak,
			isWindows: true);

		result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		scope.ExitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		scope.Token.IsCancellationRequested.Should().BeTrue();
	}

	[TestMethod]
	[Description("ControlBreak represents SIGQUIT on Unix and does not acquire the standalone SIGINT epoch.")]
	public async Task When_ControlBreakArrivesOnUnix_Then_StandaloneScopeDoesNotClaimSigQuit()
	{
		await using var scope = new ProcessSignalCancellationScope(default);

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting(
			ConsoleSpecialKey.ControlBreak,
			isWindows: false);

		result.Should().Be(ConsoleCancelKeyHandlingResult.NotHandled);
		scope.ExitCode.Should().BeNull();
		scope.Token.IsCancellationRequested.Should().BeFalse();
	}

	[TestMethod]
	[Description("Disposing the interactive claim atomically hands Ctrl+C ownership back to the active standalone scope.")]
	public async Task When_InteractiveHandlerIsDisposed_Then_StandaloneScopeClaimsCtrlC()
	{
		await using var scope = new ProcessSignalCancellationScope(default);
		using var interactiveHandler = new CancelKeyHandler();
		interactiveHandler.Dispose();

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();

		result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		scope.ExitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		scope.Token.IsCancellationRequested.Should().BeTrue();
	}

	[TestMethod]
	[Description("A late-joining standalone scope cannot reinterpret the process-wide second Ctrl+C as a first signal.")]
	public async Task When_ScopeJoinsAfterFirstCtrlC_Then_SecondCtrlCFallsThroughProcessWide()
	{
		await using var firstScope = new ProcessSignalCancellationScope(default);

		var firstSignal = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
		await using var lateScope = new ProcessSignalCancellationScope(default);
		var secondSignal = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();

		firstSignal.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		firstScope.Token.IsCancellationRequested.Should().BeTrue();
		lateScope.Token.IsCancellationRequested.Should().BeTrue();
		secondSignal.Should().Be(ConsoleCancelKeyHandlingResult.AllowProcessTermination);
	}

	[TestMethod]
	[DataRow(false, DisplayName = "Dispose old owner, then register replacement")]
	[DataRow(true, DisplayName = "Register replacement, then dispose old owner")]
	[Description("An interactive replacement registered during dispatch is reselected before standalone ownership can claim Ctrl+C.")]
	public async Task When_InteractiveOwnershipChangesDuringSelection_Then_ReplacementKeepsPriority(
		bool registerReplacementFirst)
	{
		await using var scope = new ProcessSignalCancellationScope(default);
		var initialSelectionCaptured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var releaseSelection = new ManualResetEventSlim();
		using var oldHandler = new CancelKeyHandler();
		using var oldCommandCancellation = new CancellationTokenSource();
		using var replacementCommandCancellation = new CancellationTokenSource();
		oldHandler.SetCommandCts(oldCommandCancellation);
		CancelKeyHandler? replacementHandler = null;
		try
		{
			var dispatchTask = Task.Run(() => ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting(
				ConsoleSpecialKey.ControlC,
				() =>
			{
				initialSelectionCaptured.TrySetResult();
				if (!releaseSelection.Wait(TimeSpan.FromSeconds(5)))
				{
					throw new TimeoutException("Timed out while holding the initial Ctrl+C selection.");
				}
			}));
			await initialSelectionCaptured.Task.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			if (registerReplacementFirst)
			{
				replacementHandler = new CancelKeyHandler();
				replacementHandler.SetCommandCts(replacementCommandCancellation);
				oldHandler.Dispose();
			}
			else
			{
				oldHandler.Dispose();
				replacementHandler = new CancelKeyHandler();
				replacementHandler.SetCommandCts(replacementCommandCancellation);
			}

			releaseSelection.Set();
			var result = await dispatchTask.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

			result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
			oldCommandCancellation.IsCancellationRequested.Should().BeFalse();
			replacementCommandCancellation.IsCancellationRequested.Should().BeTrue();
			scope.ExitCode.Should().BeNull();
		}
		finally
		{
			releaseSelection.Set();
			replacementHandler?.Dispose();
		}
	}

	[TestMethod]
	[Description("A draining cancellation callback keeps the process epoch alive for late scopes and second-signal escalation.")]
	public async Task When_CancellationDrainIsPending_Then_LateScopeInheritsEpoch()
	{
		var firstScope = new ProcessSignalCancellationScope(default);
		ProcessSignalCancellationScope? lateScope = null;
		var callbackStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var releaseCallback = new ManualResetEventSlim();
		using var registration = firstScope.Token.Register(() =>
		{
			callbackStarted.TrySetResult();
			if (!releaseCallback.Wait(TimeSpan.FromSeconds(5)))
			{
				throw new TimeoutException("Timed out while holding signal cancellation open.");
			}
		});

		try
		{
			var firstDispatchTask = Task.Run(() => ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting());
			await callbackStarted.Task.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			var disposeTask = firstScope.DisposeAsync().AsTask();
			disposeTask.IsCompleted.Should().BeFalse();

			lateScope = new ProcessSignalCancellationScope(default);
			lateScope.Token.IsCancellationRequested.Should().BeTrue();
			ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting()
				.Should().Be(ConsoleCancelKeyHandlingResult.AllowProcessTermination);

			releaseCallback.Set();
			await firstDispatchTask.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
			await disposeTask.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		}
		finally
		{
			releaseCallback.Set();
			await firstScope.DisposeAsync().ConfigureAwait(false);
			if (lateScope is not null)
			{
				await lateScope.DisposeAsync().ConfigureAwait(false);
			}
		}
	}

	[TestMethod]
	[Description("A cancellation callback can join the draining epoch without re-entering the process coordinator gate.")]
	public async Task When_CancellationCallbackStartsScope_Then_NewScopeIsCancelledWithoutDeadlock()
	{
		await using var firstScope = new ProcessSignalCancellationScope(default);
		var joinedScopeSource = new TaskCompletionSource<ProcessSignalCancellationScope>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		using var registration = firstScope.Token.Register(() =>
			joinedScopeSource.TrySetResult(new ProcessSignalCancellationScope(default)));

		ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting()
			.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		var joinedScope = await joinedScopeSource.Task.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await using var configuredJoinedScope = joinedScope.ConfigureAwait(false);

		joinedScope.Token.IsCancellationRequested.Should().BeTrue();
		ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting()
			.Should().Be(ConsoleCancelKeyHandlingResult.AllowProcessTermination);
	}

	[TestMethod]
	[Description("A scope remains signal-owned until its removal is atomic, so a signal in the pre-unregister window cannot be suppressed while the run still returns success.")]
	public async Task When_DisposalStartsBeforeAtomicUnregister_Then_SignalStillCancelsTheRun()
	{
		var scope = new ProcessSignalCancellationScope(default);
		var executionToken = scope.Token;
		var disposalPaused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var resumeDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var disposeTask = Task.Run(async () =>
			await scope.DisposeForTestingAsync(() =>
			{
			disposalPaused.SetResult();
			resumeDisposal.Task.GetAwaiter().GetResult();
		}).ConfigureAwait(false));

		await disposalPaused.Task.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		var signalResult = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
		resumeDisposal.SetResult();
		await disposeTask.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		signalResult.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		executionToken.IsCancellationRequested.Should().BeTrue();
		scope.ResolveExitCode(runExitCode: 0).Should().Be(130);
	}

	[TestMethod]
	[Description("Two concurrent process-signal dispatches produce exactly one cooperative first signal.")]
	public async Task When_TwoSignalsRace_Then_ExactlyOneIsSuppressed()
	{
		await using var scope = new ProcessSignalCancellationScope(default);
		using var start = new ManualResetEventSlim();
		ConsoleCancelKeyHandlingResult DispatchAfterStart()
		{
			if (!start.Wait(TimeSpan.FromSeconds(5)))
			{
				throw new TimeoutException("Timed out waiting to race process signals.");
			}

			return ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
		}

		var firstDispatch = Task.Run(DispatchAfterStart);
		var secondDispatch = Task.Run(DispatchAfterStart);

		start.Set();
		var results = await Task.WhenAll(firstDispatch, secondDispatch)
			.WaitAsync(timeout: TimeSpan.FromSeconds(5)).ConfigureAwait(false);

		results.Should().ContainSingle(
			result => result == ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		results.Should().ContainSingle(
			result => result == ConsoleCancelKeyHandlingResult.AllowProcessTermination);
	}

	[TestMethod]
	[Description("The process-wide signal claim resets after the final standalone scope is disposed.")]
	public async Task When_LastScopeIsDisposed_Then_NextScopeStartsANewSignalEpoch()
	{
		await using (var firstScope = new ProcessSignalCancellationScope(default))
		{
			ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting()
				.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		}

		await using var nextScope = new ProcessSignalCancellationScope(default);

		ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting()
			.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		nextScope.Token.IsCancellationRequested.Should().BeTrue();
	}

	[TestMethod]
	[Description("An explicit non-zero run result takes precedence over a claimed signal code, while a successful result uses the signal code.")]
	public async Task When_ResolvingExitCodeAfterSignal_Then_ExplicitFailureIsPreserved()
	{
		var scope = new ProcessSignalCancellationScope(default);
		ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
		await scope.DisposeAsync().ConfigureAwait(false);

		scope.ResolveExitCode(runExitCode: 2).Should().Be(2);
		scope.ResolveExitCode(runExitCode: 0).Should().Be(ProcessSignalCoordinator.SigIntExitCode);
	}

	[TestMethod]
	[Description("A throwing cancellation callback cannot replace the conventional signal exit policy during scope disposal.")]
	public async Task When_SignalCancellationCallbackThrows_Then_DisposalStillCompletes()
	{
		using var error = new StringWriter();
		using var session = ReplSessionIO.SetSession(TextWriter.Null, TextReader.Null, error: error);
		var scope = new ProcessSignalCancellationScope(default);
		using var registration = scope.Token.Register(
			static () => throw new InvalidOperationException("callback failure"));

		var result = ConsoleCancelKeyCoordinator.HandleCancelKeyForTesting();
		var act = async () => await scope.DisposeAsync().ConfigureAwait(false);

		result.Should().Be(ConsoleCancelKeyHandlingResult.SuppressProcessTermination);
		await act.Should().NotThrowAsync().ConfigureAwait(false);
		scope.ExitCode.Should().Be(ProcessSignalCoordinator.SigIntExitCode);
		error.ToString().Should().Contain("process-signal cancellation callback")
			.And.Contain(nameof(InvalidOperationException));
	}

	[TestMethod]
	[Description("Mac Catalyst remains unsupported because .NET compiles the mobile PosixSignalRegistration implementation there.")]
	public void When_PlatformIsInTheIOSFamily_Then_SignalBridgeIsUnsupported()
	{
		ProcessSignalCoordinator.IsSignalBridgeSupportedForTesting(
			isAndroid: false,
			isBrowser: false,
			isIOS: true,
			isTvOS: false).Should().BeFalse();
	}

}
