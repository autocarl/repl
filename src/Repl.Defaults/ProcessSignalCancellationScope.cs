using System.Runtime.InteropServices;

namespace Repl;

internal sealed class ProcessSignalCancellationScope : IAsyncDisposable
{
	private const int SigTermExitCode = 143;

	private readonly CancellationTokenSource _signalCancellation = new();
	private readonly CancellationTokenSource _linkedCancellation;
	private readonly PosixSignalRegistration _sigTermRegistration;
	private readonly Lock _gate = new();
	private Task _cancellationTask = Task.CompletedTask;
	private int _exitCode;
	private bool _disposed;

	public ProcessSignalCancellationScope(CancellationToken cancellationToken)
	{
		_linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
			cancellationToken,
			_signalCancellation.Token);

		try
		{
			_sigTermRegistration = PosixSignalRegistration.Create(
				PosixSignal.SIGTERM,
				HandleSignal);
		}
		catch
		{
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

		_sigTermRegistration.Dispose();
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

	private void HandleSignal(PosixSignalContext context)
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
