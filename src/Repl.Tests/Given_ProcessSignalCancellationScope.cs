using AwesomeAssertions;
using System.Reflection;

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
		var args = CreateCancelEventArgs(ConsoleSpecialKey.ControlC);

		scope.HandleCancelKey(sender: null, args);

		args.Cancel.Should().BeFalse();
		scope.ExitCode.Should().Be(0);
		scope.Token.IsCancellationRequested.Should().BeFalse();
	}

	private static ConsoleCancelEventArgs CreateCancelEventArgs(ConsoleSpecialKey specialKey) =>
		(ConsoleCancelEventArgs)(Activator.CreateInstance(
			typeof(ConsoleCancelEventArgs),
			BindingFlags.Instance | BindingFlags.NonPublic,
			binder: null,
			args: [specialKey],
			culture: null)
		?? throw new InvalidOperationException("Could not construct console cancel event arguments."));
}
