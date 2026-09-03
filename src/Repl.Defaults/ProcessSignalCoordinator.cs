using System.Runtime.InteropServices;

namespace Repl;

/// <summary>
/// Owns the process-wide standalone signal protocol. The first SIGINT (or SIGTERM on Unix)
/// atomically claims the current ownership epoch and prepares cancellation for every active scope;
/// later scopes join that draining epoch, and any subsequent signal is left to the operating-system
/// default. The epoch resets only after its final scope and all signal-triggered cancellation work
/// have drained. Interactive Ctrl+C ownership is selected first by
/// <see cref="ConsoleCancelKeyCoordinator"/>. Process registrations are installed lazily once and
/// remain inert when there are no active standalone scopes.
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

	internal static void Register(ProcessSignalCancellationScope scope)
	{
		ArgumentNullException.ThrowIfNull(scope);
		RegistrationFailure? registrationFailure;
		Action? startCancellation = null;
		lock (Gate)
		{
			registrationFailure = TryInitializeRegistrations();
			if (registrationFailure is null)
			{
				ActiveScopes.Add(scope);
				if (s_claimedSignal is { } claimedSignal)
				{
					startCancellation = scope.PrepareSignalCancellation(claimedSignal.ExitCode);
				}
			}
		}

		if (registrationFailure is { } failure)
		{
			failure.CancelKeyRegistration?.Dispose();
			failure.SigTermRegistration?.Dispose();
			System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure.Exception).Throw();
		}

		startCancellation?.Invoke();
	}

	private static RegistrationFailure? TryInitializeRegistrations()
	{
		if (s_registrationsInitialized)
		{
			return null;
		}

		if (!IsSignalBridgeSupported())
		{
			s_registrationsInitialized = true;
			return null;
		}

		PosixSignalRegistration? sigTermRegistration = null;
		IDisposable? cancelKeyRegistration = null;
		var generation = ++s_generation;
		try
		{
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
			return new RegistrationFailure(ex, cancelKeyRegistration, sigTermRegistration);
		}
	}

	internal static async Task UnregisterAsync(ProcessSignalCancellationScope scope)
	{
		ArgumentNullException.ThrowIfNull(scope);
		Task cancellationTask;
		lock (Gate)
		{
			cancellationTask = scope.MarkDisposedAndGetCancellationTaskAsync();
			ActiveScopes.Remove(scope);
			s_pendingDrainCount++;
		}

		try
		{
#pragma warning disable VSTHRD003 // Signal-triggered callbacks must drain before their epoch can reset.
			await cancellationTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
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

		foreach (var startCancellation in startCancellations!)
		{
			startCancellation();
		}

		WriteDiagnostic(
			$"Received {name}; cancelling active standalone runs. Send the signal again to terminate immediately.");
		return ConsoleCancelKeyHandlingResult.SuppressProcessTermination;
	}

	private static bool IsSignalBridgeSupported() =>
		!OperatingSystem.IsAndroid()
		&& !OperatingSystem.IsBrowser()
		&& !OperatingSystem.IsIOS()
		&& !OperatingSystem.IsTvOS();

	private static void WriteDiagnostic(string message)
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

	private readonly record struct ClaimedSignal(string Name, int ExitCode);

	private sealed record RegistrationFailure(
		Exception Exception,
		IDisposable? CancelKeyRegistration,
		PosixSignalRegistration? SigTermRegistration);
}
