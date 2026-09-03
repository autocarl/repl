using System.Diagnostics;
using AwesomeAssertions;

namespace Repl.IntegrationTests;

[TestClass]
public sealed class Given_ProcessSignals
{
	private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(15);

	[TestMethod]
	[Description("A standalone one-shot run converts SIGTERM into cooperative cancellation before exiting.")]
	public async Task When_StandaloneRunReceivesSigTerm_Then_FinallyRunsAndConventionalExitCodeIsReturned()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Inconclusive("POSIX signal delivery is exercised on Unix runners.");
		}

		var marker = Path.Combine(Path.GetTempPath(), $"repl-signal-{Guid.NewGuid():N}.txt");
		using var process = ShellCompletionTestHostRunner.Start(
			"process-signal",
			["wait", marker, "--no-logo"]);
		try
		{
			await WaitForMarkerAsync(process, marker, "READY").ConfigureAwait(false);

			await SendSignalAsync(process, signal: 15).ConfigureAwait(false);
			await process.WaitForExitAsync().WaitAsync(ProcessTimeout).ConfigureAwait(false);

			process.ExitCode.Should().Be(143);
			(await File.ReadAllLinesAsync(marker).ConfigureAwait(false)).Should().Equal("READY", "FINALLY");
		}
		finally
		{
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
				await process.WaitForExitAsync().WaitAsync(ProcessTimeout).ConfigureAwait(false);
			}

			File.Delete(marker);
		}
	}

	private static async Task WaitForMarkerAsync(Process process, string path, string marker)
	{
		var deadline = DateTime.UtcNow + ProcessTimeout;
		while (DateTime.UtcNow < deadline)
		{
			if (File.Exists(path)
				&& (await File.ReadAllLinesAsync(path).ConfigureAwait(false)).Contains(marker, StringComparer.Ordinal))
			{
				return;
			}

			if (process.HasExited)
			{
				throw new InvalidOperationException(
					$"Signal test host exited with code {process.ExitCode} before writing {marker}.");
			}

			await Task.Delay(TimeSpan.FromMilliseconds(25)).ConfigureAwait(false);
		}

		throw new TimeoutException($"Signal test host did not write {marker} within {ProcessTimeout}.");
	}

	private static async Task SendSignalAsync(Process target, int signal)
	{
		var startInfo = new ProcessStartInfo("kill")
		{
			UseShellExecute = false,
			RedirectStandardError = true,
		};
		startInfo.ArgumentList.Add($"-{signal}");
		startInfo.ArgumentList.Add(target.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
		using var sender = Process.Start(startInfo)
			?? throw new InvalidOperationException("Failed to start the signal sender.");
		await sender.WaitForExitAsync().WaitAsync(ProcessTimeout).ConfigureAwait(false);
		var error = await sender.StandardError.ReadToEndAsync().ConfigureAwait(false);
		sender.ExitCode.Should().Be(0, because: $"the test signal must reach the child process: {error}");
	}
}
