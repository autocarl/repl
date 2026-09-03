using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Repl.Tests;

[TestClass]
public sealed class Given_HandlerBinding
{
	[TestMethod]
	[Description("Regression guard: verifies binding route and positional parameters so that handler receives converted values.")]
	public void When_BindingRouteAndPositionalParameters_Then_HandlerReceivesConvertedValues()
	{
		var sut = ReplApp.Create();
		var capturedId = 0;
		var capturedName = string.Empty;

		sut.Map("contact {id:int} rename", (int id, string name) =>
		{
			capturedId = id;
			capturedName = name;
			return "ok";
		});

		var exitCode = sut.Run(["contact", "42", "rename", "alice"]);

		exitCode.Should().Be(0);
		capturedId.Should().Be(42);
		capturedName.Should().Be("alice");
	}

	[TestMethod]
	[Description("Regression guard: verifies binding named option so that option value is mapped by parameter name.")]
	public void When_BindingNamedOption_Then_OptionValueIsMappedByParameterName()
	{
		var sut = ReplApp.Create();
		var capturedLimit = 0;

		sut.Map("contact list", (int limit) =>
		{
			capturedLimit = limit;
			return "ok";
		});

		var exitCode = sut.Run(["contact", "list", "--limit", "5"]);

		exitCode.Should().Be(0);
		capturedLimit.Should().Be(5);
	}

	[TestMethod]
	[Description("Regression guard: verifies binding repeated named option so that list parameter contains all values.")]
	public void When_BindingRepeatedNamedOption_Then_ListParameterContainsAllValues()
	{
		var sut = ReplApp.Create();
		List<string>? captured = null;

		sut.Map("contact tag", (List<string> tag) =>
		{
			captured = tag;
			return "ok";
		});

		var exitCode = sut.Run(["contact", "tag", "--tag", "vip", "--tag", "priority"]);

		exitCode.Should().Be(0);
		captured.Should().NotBeNull();
		captured!.Should().ContainInOrder("vip", "priority");
	}

	[TestMethod]
	[Description("Regression guard: verifies binding variadic positional array so that all remaining tokens are collected.")]
	public void When_BindingVariadicPositionalArray_Then_AllRemainingTokensAreCollected()
	{
		var sut = ReplApp.Create();
		int[]? captured = null;

		sut.Map("delete", (int[] ids) =>
		{
			captured = ids;
			return "ok";
		});

		var exitCode = sut.Run(["delete", "1", "2", "3"]);

		exitCode.Should().Be(0);
		captured.Should().NotBeNull();
		captured!.Should().ContainInOrder(1, 2, 3);
	}

	[TestMethod]
	[Description("Regression guard: verifies binding service parameter so that dependency is resolved from container.")]
	public void When_BindingServiceParameter_Then_DependencyIsResolvedFromContainer()
	{
		var sut = ReplApp.Create(services =>
		{
			services.AddSingleton<ITestCounter>(new TestCounter(7));
		});
		var captured = 0;

		sut.Map("counter", (ITestCounter counter) =>
		{
			captured = counter.Value;
			return "ok";
		});

		var exitCode = sut.Run(["counter"]);

		exitCode.Should().Be(0);
		captured.Should().Be(7);
	}

	[TestMethod]
	[Description("Regression guard: verifies binding cancellation token so that handler receives execution token.")]
	public async Task When_BindingCancellationToken_Then_HandlerReceivesExecutionToken()
	{
		var sut = ReplApp.Create();
		CancellationToken captured = default;
		using var cancellationTokenSource = new CancellationTokenSource();

		sut.Map("work", async (CancellationToken ct) =>
		{
			captured = ct;
			await Task.Yield();
			return "ok";
		});

		var exitCode = await sut.RunAsync(
			["work"],
			new ReplRunOptions { ProcessSignalHandling = ProcessSignalHandlingMode.None },
			cancellationTokenSource.Token).ConfigureAwait(false);

		exitCode.Should().Be(0);
		captured.CanBeCanceled.Should().BeTrue();
		captured.Should().Be(cancellationTokenSource.Token);
	}

	[TestMethod]
	[Description("Embedded console profile keeps process signal ownership with its caller by default.")]
	public async Task When_UsingEmbeddedConsoleProfile_Then_HandlerReceivesCallerTokenDirectly()
	{
		var sut = ReplApp.Create().UseEmbeddedConsoleProfile();
		CancellationToken captured = default;
		using var cancellationTokenSource = new CancellationTokenSource();

		sut.Map("work", (CancellationToken ct) =>
		{
			captured = ct;
			return "ok";
		});

		var exitCode = await sut.RunAsync(["work"], cancellationTokenSource.Token).ConfigureAwait(false);

		exitCode.Should().Be(0);
		captured.Should().Be(cancellationTokenSource.Token);
	}

	[TestMethod]
	[Description("Unrelated per-run settings preserve the embedded profile's caller-owned signal default.")]
	public async Task When_UsingEmbeddedConsoleProfileWithUnrelatedRunOptions_Then_HandlerReceivesCallerTokenDirectly()
	{
		var sut = ReplApp.Create().UseEmbeddedConsoleProfile();
		CancellationToken captured = default;
		using var cancellationTokenSource = new CancellationTokenSource();

		sut.Map("work", (CancellationToken ct) =>
		{
			captured = ct;
			return "ok";
		});

		var exitCode = await sut.RunAsync(
			["work"],
			new ReplRunOptions { AnsiSupport = AnsiMode.Never },
			cancellationTokenSource.Token).ConfigureAwait(false);

		exitCode.Should().Be(0);
		captured.Should().Be(cancellationTokenSource.Token);
	}

	[TestMethod]
	[Description("An embedded console can explicitly opt into automatic process-signal ownership for one run.")]
	public async Task When_EmbeddedConsoleExplicitlySelectsAutomatic_Then_HandlerReceivesLinkedToken()
	{
		var sut = ReplApp.Create().UseEmbeddedConsoleProfile();
		CancellationToken captured = default;
		using var cancellationTokenSource = new CancellationTokenSource();

		sut.Map("work", (CancellationToken ct) =>
		{
			captured = ct;
			return "ok";
		});

		var exitCode = await sut.RunAsync(
			["work"],
			new ReplRunOptions { ProcessSignalHandling = ProcessSignalHandlingMode.Automatic },
			cancellationTokenSource.Token).ConfigureAwait(false);

		exitCode.Should().Be(0);
		captured.Should().NotBe(cancellationTokenSource.Token);
		captured.CanBeCanceled.Should().BeTrue();
	}

	[TestMethod]
	[Description("The linked execution token used by automatic process-signal handling is disposed when its run completes.")]
	public async Task When_AutomaticRunCompletes_Then_HandlerTokenMustNotBeRetained()
	{
		var sut = ReplApp.Create();
		CancellationToken captured = default;

		sut.Map("work", (CancellationToken ct) =>
		{
			captured = ct;
			return "ok";
		});

		var exitCode = await sut.RunAsync(["work", "--no-logo"]).ConfigureAwait(false);
		var accessDisposedWaitHandle = () => _ = captured.WaitHandle;

		exitCode.Should().Be(0);
		accessDisposedWaitHandle.Should().Throw<ObjectDisposedException>();
	}

	[TestMethod]
	[Description("Regression guard: verifies automatic signal handling preserves caller-requested cancellation through the linked execution token.")]
	public async Task When_ProcessSignalHandlingIsAutomatic_Then_HandlerObservesCallerCancellation()
	{
		var sut = ReplApp.Create();
		using var cancellationTokenSource = new CancellationTokenSource();
		var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var observedCancellation = false;

		sut.Map("work", async (CancellationToken ct) =>
		{
			handlerStarted.SetResult();
			try
			{
				await Task.Delay(Timeout.InfiniteTimeSpan, ct).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (ct.IsCancellationRequested)
			{
				observedCancellation = true;
				throw;
			}
		});

		var runTask = sut.RunAsync(["work"], cancellationTokenSource.Token).AsTask();
		await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
		await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

#pragma warning disable VSTHRD003 // The run must start before this test requests caller cancellation.
		var act = async () => await runTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003
		await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
		observedCancellation.Should().BeTrue();
	}

	[TestMethod]
	[Description("Regression guard: verifies handler returns task of result so that exit code reflects resolved result.")]
	public void When_HandlerReturnsTaskOfResult_Then_ExitCodeReflectsResolvedResult()
	{
		var sut = ReplApp.Create();
		sut.Map("async-task", static async () =>
		{
			await Task.Yield();
			return Results.NotFound("missing");
		});

		var exitCode = sut.Run(["async-task", "--no-logo"]);

		exitCode.Should().Be(1);
	}

	[TestMethod]
	[Description("Regression guard: verifies handler returns value task of result so that exit code reflects resolved result.")]
	public void When_HandlerReturnsValueTaskOfResult_Then_ExitCodeReflectsResolvedResult()
	{
		var sut = ReplApp.Create();
		sut.Map("async-valuetask", static () =>
			ValueTask.FromResult<IReplResult>(Results.NotFound("missing")));

		var exitCode = sut.Run(["async-valuetask", "--no-logo"]);

		exitCode.Should().Be(1);
	}

	[TestMethod]
	[Description("Result-flow paging context is injected so handlers can page data at the source.")]
	public void When_HandlerRequestsPagingContext_Then_ResultFlowOptionsAreAvailable()
	{
		var sut = ReplApp.Create();
		IReplPagingContext? captured = null;
		ReplPage<string>? page = null;

		sut.Map("users list", (IReplPagingContext paging) =>
		{
			captured = paging;
			page = paging.Page(["Alice", "Bob"], nextCursor: "next", totalCount: 3);
			return "ok";
		});

		var exitCode = sut.Run(
			["users", "list", "--result:page-size=2", "--result:cursor=start", "--no-logo"]);

		exitCode.Should().Be(0);
		captured.Should().NotBeNull();
		captured!.SuggestedPageSize.Should().Be(2);
		captured.Cursor.Should().Be("start");
		captured.MaxPageSize.Should().BeGreaterThanOrEqualTo(2);
		page.Should().NotBeNull();
		page!.Items.Should().Equal("Alice", "Bob");
		page.PageInfo.Cursor.Should().Be("start");
		page.PageInfo.NextCursor.Should().Be("next");
		page.PageInfo.TotalCount.Should().Be(3);
		page.PageInfo.HasMore.Should().BeTrue();
	}

	private interface ITestCounter
	{
		int Value { get; }
	}

	private sealed class TestCounter(int value) : ITestCounter
	{
		public int Value { get; } = value;
	}
}





