using System.Runtime.InteropServices;

namespace Repl;

internal sealed class ProcessSignalCancellationScope : IAsyncDisposable
{
	private const int SigIntExitCode = 130;
	private const int SigTermExitCode = 143;

	private readonly CancellationTokenSource _signalCancellation = new();
	private readonly CancellationTokenSource _linkedCancellation;
	private readonly PosixSignalRegistration? _sigTermRegistration;
	private readonly Lock _gate = new();
	private Task _cancellationTask = Task.CompletedTask;
	private int _exitCode;
	private bool _disposed;

	public ProcessSignalCancellationScope(CancellationToken cancellationToken)
	{
		_linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			_signalCancellation.Token);

		var cancelKeyRegistered = false;
		try
		{
			Console.CancelKeyPress += HandleCancelKey;
			cancelKeyRegistered = true;
			if (!OperatingSystem.IsWindows())
			{
				_sigTermRegistration = PosixSignalRegistration.Create(
					PosixSignal.SIGTERM,
					HandleSigTerm);
			}
		}
		catch
		{
			if (cancelKeyRegistered)
			{
				Console.CancelKeyPress -= HandleCancelKey;
			}

			_linkedCancellation.Dispose();
			_signalCancellation.Dispose();
			throw;
		}
	}

	public CancellationToken Token => _linkedCancellation.Token;

	public int ExitCode => Volatile.Read(ref _exitCode);

	public async ValueTask DisposeAsync()
	{
		Task cancellationTask;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			cancellationTask = _cancellationTask;
		}

		Console.CancelKeyPress -= HandleCancelKey;
		_sigTermRegistration?.Dispose();
		try
		{
#pragma warning disable VSTHRD003 // The OS signal callback starts this task; disposal must observe its completion.
			await cancellationTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
		}
		finally
		{
			_linkedCancellation.Dispose();
			_signalCancellation.Dispose();
		}
	}

	internal void HandleCancelKey(object? sender, ConsoleCancelEventArgs context)
	{
		_ = sender;
		if (context.SpecialKey != ConsoleSpecialKey.ControlC
			|| CancelKeyHandler.HasActiveConsoleHandler)
		{
			return;
		}

		lock (_gate)
		{
			if (_disposed || _exitCode != 0 || CancelKeyHandler.HasActiveConsoleHandler)
			{
				return;
			}

			_exitCode = SigIntExitCode;
			context.Cancel = true;
			_cancellationTask = _signalCancellation.CancelAsync();
		}
	}

	private void HandleSigTerm(PosixSignalContext context)
	{
		lock (_gate)
		{
			if (_disposed || _exitCode != 0)
			{
				return;
			}

			_exitCode = SigTermExitCode;
			context.Cancel = true;
			_cancellationTask = _signalCancellation.CancelAsync();
		}
	}
}
