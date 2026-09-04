using System.Runtime.InteropServices;

namespace Repl;

/// <summary>
/// Owns the process-wide standalone signal protocol. The first SIGINT (or SIGTERM on Unix)
/// atomically claims the current ownership epoch and prepares cancellation for every active scope;
/// later scopes join that draining epoch, and any subsequent signal is left to the operating-system
/// default. The epoch resets only after its final scope and all signal-triggered cancellation work
/// have drained. Interactive console-key ownership is selected first by
/// <see cref="ConsoleCancelKeyCoordinator"/>. Its gate is always released before this coordinator is
/// invoked. This coordinator may acquire an individual scope gate, but a scope never enters this
/// coordinator while holding its gate; reserved consumer callbacks start only after both gates are
/// released. Process registrations are installed lazily once and remain inert without active scopes.
/// </summary>
internal static class ProcessSignalCoordinator
{
	internal const int SigIntExitCode = 130;
	internal const int SigTermExitCode = 143;

	private static readonly Lock Gate = new();
	private static readonly HashSet<ProcessSignalCancellationScope> ActiveScopes = [];
	private static IDisposable? s_cancelKeyRegistration;
	private static PosixSignalRegistration? s_sigTermRegistration;
	private static ClaimedSignal? s_claimedSignal;
	private static int s_generation;
	private static int s_pendingDrainCount;
	private static bool s_registrationsInitialized;
	private static Exception? s_registrationFaultForTesting;

	/// <summary>
	/// Forces the next registration attempt to fail, then restores the coordinator when disposed.
	/// The registrations are process-wide and latch on first use, so a test that exercises the
	/// failure policy has to put the coordinator back the way it found it.
	/// </summary>
	internal static IDisposable InjectRegistrationFaultForTesting(Exception fault)
	{
		ArgumentNullException.ThrowIfNull(fault);
		return new RegistrationFaultScope(fault);
	}

	internal static void Register(ProcessSignalCancellationScope scope)
	{
		ArgumentNullException.ThrowIfNull(scope);
		RegistrationFailure? registrationFailure;
		var bridgeUnavailable = false;
		Action? startCancellation = null;
		lock (Gate)
		{
			registrationFailure = TryInitializeRegistrations(out bridgeUnavailable);
			// The scope joins the epoch even when no bridge could be installed, so that disposal stays
			// symmetric and a run started before an earlier scope claimed a signal still inherits it.
			ActiveScopes.Add(scope);
			if (s_claimedSignal is { } claimedSignal)
			{
				startCancellation = scope.PrepareSignalCancellation(claimedSignal.ExitCode);
			}
		}

		if (bridgeUnavailable)
		{
			WriteDiagnostic(
				"Automatic process-signal handling is unavailable on this platform; "
				+ "the caller or platform host remains responsible for cancellation.");
		}

		if (registrationFailure is { } failure)
		{
			failure.CancelKeyRegistration?.Dispose();
			failure.SigTermRegistration?.Dispose();
			// Installing the bridge is a convenience, not a precondition for running the command, so a
			// rejected registration degrades to caller-owned handling exactly like an unsupported
			// platform. Aborting here would turn every run in a restricted environment into a failure.
			WriteDiagnostic(
				"Failed to install automatic process-signal handling: "
				+ $"{failure.Exception.GetType().Name}: {failure.Exception.Message}. "
				+ "The caller or platform host remains responsible for cancellation.");
		}

		startCancellation?.Invoke();
	}

	private static RegistrationFailure? TryInitializeRegistrations(out bool bridgeUnavailable)
	{
		bridgeUnavailable = false;
		if (s_registrationsInitialized)
		{
			return null;
		}

		if (!IsSignalBridgeSupported())
		{
			s_registrationsInitialized = true;
			bridgeUnavailable = true;
			return null;
		}

		PosixSignalRegistration? sigTermRegistration = null;
		IDisposable? cancelKeyRegistration = null;
		var generation = ++s_generation;
		try
		{
			// No supported platform rejects a signal registration on demand, so the failure policy
			// would otherwise be untestable. Tests set this to drive Register through the failing path.
			if (s_registrationFaultForTesting is { } injectedFault)
			{
				throw injectedFault;
			}

			if (!OperatingSystem.IsWindows())
			{
				sigTermRegistration = PosixSignalRegistration.Create(
					PosixSignal.SIGTERM,
					e => HandleSigTerm(generation, e));
			}

			cancelKeyRegistration = ConsoleCancelKeyCoordinator.RegisterStandalone(
				() => HandleSigInt(generation));
			s_sigTermRegistration = sigTermRegistration;
			s_cancelKeyRegistration = cancelKeyRegistration;
			s_registrationsInitialized = true;
			return null;
		}
		catch (Exception ex)
		{
			// Invalidate callbacks created by the failed generation before releasing the gate.
			s_generation++;
			// Latch the attempt: retrying on every later run would repeat a failure the environment
			// has already refused, and would emit the same diagnostic once per run.
			s_registrationsInitialized = true;
			return new RegistrationFailure(ex, cancelKeyRegistration, sigTermRegistration);
		}
	}

	internal static async Task<Exception?> UnregisterAsync(ProcessSignalCancellationScope scope)
	{
		ArgumentNullException.ThrowIfNull(scope);
		Task cancellationTask;
		lock (Gate)
		{
			cancellationTask = scope.MarkDisposedAndGetCancellationTaskAsync();
			ActiveScopes.Remove(scope);
			s_pendingDrainCount++;
		}

		Exception? cancellationCallbackException = null;
		try
		{
#pragma warning disable VSTHRD003 // Signal-triggered callbacks must drain before their epoch can reset.
			await cancellationTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}
		catch (Exception ex)
		{
			// This task exclusively represents CancellationToken callbacks reserved by the scope.
			cancellationCallbackException = ex;
		}
		finally
		{
			lock (Gate)
			{
				s_pendingDrainCount--;
				if (ActiveScopes.Count == 0 && s_pendingDrainCount == 0)
				{
					s_claimedSignal = null;
				}
			}
		}

		return cancellationCallbackException;
	}

	private static ConsoleCancelKeyHandlingResult HandleSigInt(int generation) =>
		TryClaimSignal(generation, "SIGINT", SigIntExitCode);

	private static void HandleSigTerm(int generation, PosixSignalContext e)
	{
		if (TryClaimSignal(generation, "SIGTERM", SigTermExitCode)
			== ConsoleCancelKeyHandlingResult.SuppressProcessTermination)
		{
			e.Cancel = true;
		}
	}

	private static ConsoleCancelKeyHandlingResult TryClaimSignal(
		int generation,
		string name,
		int exitCode)
	{
		ClaimedSignal? previousSignal;
		List<Action>? startCancellations = null;
		lock (Gate)
		{
			if (generation != s_generation)
			{
				return ConsoleCancelKeyHandlingResult.NotHandled;
			}

			previousSignal = s_claimedSignal;
			if (ActiveScopes.Count == 0 && previousSignal is null)
			{
				return ConsoleCancelKeyHandlingResult.NotHandled;
			}

			if (previousSignal is null)
			{
				s_claimedSignal = new ClaimedSignal(name, exitCode);
				startCancellations = [];
				foreach (var scope in ActiveScopes)
				{
					if (scope.PrepareSignalCancellation(exitCode) is { } startCancellation)
					{
						startCancellations.Add(startCancellation);
					}
				}
			}
		}

		if (previousSignal is { } claimedSignal)
		{
			WriteDiagnostic(
				$"Received {name} after {claimedSignal.Name}; allowing immediate operating-system termination.");
			return ConsoleCancelKeyHandlingResult.AllowProcessTermination;
		}

		if (startCancellations is not { } cancellations)
		{
			return ConsoleCancelKeyHandlingResult.NotHandled;
		}

		foreach (var startCancellation in cancellations)
		{
			startCancellation();
		}

		WriteDiagnostic(
			$"Received {name}; cancelling active standalone runs. Send the signal again to terminate immediately.");
		return ConsoleCancelKeyHandlingResult.SuppressProcessTermination;
	}

	private static bool IsSignalBridgeSupported() =>
		IsSignalBridgeSupportedForTesting(
			OperatingSystem.IsAndroid(),
			OperatingSystem.IsBrowser(),
			OperatingSystem.IsIOS(),
			OperatingSystem.IsTvOS());

	internal static bool IsSignalBridgeSupportedForTesting(
		bool isAndroid,
		bool isBrowser,
		bool isIOS,
		bool isTvOS) =>
		!isAndroid
		&& !isBrowser
		&& !isIOS
		&& !isTvOS;

	internal static void WriteDiagnostic(string message)
	{
		try
		{
#pragma warning disable MA0045 // Process-signal callbacks must decide synchronously before the OS resumes default handling.
			ReplSessionIO.Error.WriteLine(message);
#pragma warning restore MA0045
		}
		catch (Exception ex) when (ex is IOException or ObjectDisposedException)
		{
			// Signal delivery must not fail merely because the diagnostic stream is unavailable.
		}
	}

	private sealed class RegistrationFaultScope : IDisposable
	{
		private readonly IDisposable? _previousCancelKeyRegistration;
		private readonly PosixSignalRegistration? _previousSigTermRegistration;
		private readonly bool _wasInitialized;

		public RegistrationFaultScope(Exception fault)
		{
			lock (Gate)
			{
				_previousCancelKeyRegistration = s_cancelKeyRegistration;
				_previousSigTermRegistration = s_sigTermRegistration;
				_wasInitialized = s_registrationsInitialized;
				s_cancelKeyRegistration = null;
				s_sigTermRegistration = null;
				s_registrationsInitialized = false;
				s_registrationFaultForTesting = fault;
			}
		}

		public void Dispose()
		{
			IDisposable? installedCancelKeyRegistration;
			PosixSignalRegistration? installedSigTermRegistration;
			lock (Gate)
			{
				s_registrationFaultForTesting = null;
				installedCancelKeyRegistration = s_cancelKeyRegistration;
				installedSigTermRegistration = s_sigTermRegistration;
				s_cancelKeyRegistration = _previousCancelKeyRegistration;
				s_sigTermRegistration = _previousSigTermRegistration;
				s_registrationsInitialized = _wasInitialized;
				// Callbacks created while the fault was installed must never fire afterwards.
				s_generation++;
			}

			installedCancelKeyRegistration?.Dispose();
			installedSigTermRegistration?.Dispose();
		}
	}

	private readonly record struct ClaimedSignal(string Name, int ExitCode);

	private sealed record RegistrationFailure(
		Exception Exception,
		IDisposable? CancelKeyRegistration,
		PosixSignalRegistration? SigTermRegistration);
}
