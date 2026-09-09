using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Repl.Tests;

[TestClass]
public sealed class Given_ExitCodes
{
	[TestMethod]
	[Description("Regression guard: verifies a text-returning handler is classified Success so that the process exits 0.")]
	public void When_HandlerReturnsText_Then_KindIsSuccessAndExitCodeIsZero()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["hello"], out _);

		exitCode.Should().Be(0);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Success);
	}

	[TestMethod]
	[Description("Regression guard: verifies --help is classified Help so that scripted callers can tell a no-op from real work.")]
	public void When_HelpIsRequested_Then_KindIsHelpAndExitCodeIsZero()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["--help"], out _);

		exitCode.Should().Be(0);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Help);
	}

	[TestMethod]
	[Description("Regression guard: verifies a bare invocation that prints help is classified Help and keeps exiting 0 by default.")]
	public void When_BareInvocationPrintsHelp_Then_KindIsHelpAndExitCodeIsZero()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, [], out var output);

		exitCode.Should().Be(0);
		output.Should().Contain("hello");
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Help);
	}

	[TestMethod]
	[Description("Regression guard: verifies the Help code is configurable so that a bare invocation can fail a CI step that ran the tool with no arguments.")]
	public void When_HelpIsMappedToNonZero_Then_BareInvocationReturnsMappedCode()
	{
		var sut = CreateApp(recorder: null, options => options.ExitCodes.Help = 64);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, [], out _);

		exitCode.Should().Be(64);
	}

	[TestMethod]
	[DataRow("unknown command", new[] { "nope" })]
	[DataRow("ambiguous prefix", new[] { "contact", "l" })]
	[DataRow("unknown command option", new[] { "hello", "--bogus", "x" })]
	[DataRow("global option missing its value", new[] { "hello", "--output" })]
	[DataRow("unknown output format", new[] { "hello", "--output:toml" })]
	[Description("Regression guard: verifies every framework refusal is a UsageError with exit code 2 so that misuse stays distinguishable from a handler failure.")]
	public void When_InvocationIsRefused_Then_KindIsUsageErrorAndExitCodeIsTwo(string refusal, string[] args)
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", (string? name) => name ?? "world");
		sut.Map("contact list", () => "list");
		sut.Map("contact load", () => "load");

		var exitCode = Run(sut, args, out _);

		exitCode.Should().Be(2, refusal);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError, refusal);
	}

	[TestMethod]
	[Description("Regression guard: verifies a routing refusal hands the rendered refusal result to the resolver so that a consumer can map on the framework's own diagnostic instead of parsing text.")]
	public void When_CommandIsUnknown_Then_OutcomeCarriesTheRenderedRefusal()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		_ = Run(sut, ["nope"], out _);

		recorder.Last!.Result.Should().BeAssignableTo<IReplResult>();
	}

	[TestMethod]
	[Description("Regression guard: verifies a missing required parameter is a BindingError with exit code 2 so that binding failures are distinct from handler failures.")]
	public void When_RequiredParameterIsMissing_Then_KindIsBindingErrorAndExitCodeIsTwo()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("set", (int value) => value);

		var exitCode = Run(sut, ["set"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.BindingError);
		recorder.Last.Exception.Should().NotBeNull();
	}

	[TestMethod]
	[Description("Regression guard: verifies a parameter conversion failure is a BindingError so that invalid values report the binding code.")]
	public void When_ParameterConversionFails_Then_KindIsBindingError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("set", (int value) => value);

		var exitCode = Run(sut, ["set", "abc"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.BindingError);
	}

	[TestMethod]
	[DataRow("error")]
	[DataRow("validation")]
	[DataRow("not_found")]
	[DataRow("cancelled")]
	[Description("Regression guard: verifies handler-returned failure results are HandlerError with exit code 1 so that existing handler contracts keep their code.")]
	public void When_HandlerReturnsFailureResult_Then_KindIsHandlerErrorAndExitCodeIsOne(string kind)
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("fail", () => kind switch
		{
			"error" => Results.Error("boom", "failed"),
			"validation" => Results.Validation("invalid"),
			"not_found" => Results.NotFound("missing"),
			_ => Results.Cancelled("stopped"),
		});

		var exitCode = Run(sut, ["fail"], out _);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerError);
		recorder.Last.Result.Should().BeAssignableTo<IReplResult>();
	}

	[TestMethod]
	[Description("Regression guard: verifies an explicit IExitResult bypasses the table so that handler-owned codes are never remapped silently.")]
	public void When_HandlerReturnsExitResult_Then_CodePassesThroughAndKindIsHandlerExitCode()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.HandlerError = 7);
		sut.Map("quit", () => Results.Exit(42));

		var exitCode = Run(sut, ["quit"], out _);

		exitCode.Should().Be(42);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerExitCode);
		recorder.Last.ExitCode.Should().Be(42);
	}

	[TestMethod]
	[Description("Regression guard: verifies an unhandled handler exception is HandlerException and exposes the unwrapped exception to the resolver.")]
	public void When_HandlerThrows_Then_KindIsHandlerExceptionAndExceptionIsExposed()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("boom", Boom);

		var exitCode = Run(sut, ["boom"], out _);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerException);
		recorder.Last.Exception!.Message.Should().Be("boom");

		static string Boom() => throw new FormatException("boom");
	}

	[TestMethod]
	[Description("Regression guard: verifies a handler-thrown InvalidOperationException is HandlerException, not BindingError, so that binder failures stay distinguishable.")]
	public void When_HandlerThrowsInvalidOperationException_Then_KindIsHandlerExceptionNotBindingError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.BindingError = 9);
		sut.Map("boom", Boom);

		var exitCode = Run(sut, ["boom"], out var output);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerException);
		output.Should().Contain("boom");

		static string Boom() => throw new InvalidOperationException("boom");
	}

	[TestMethod]
	[Description("Regression guard: verifies cancellation still propagates as an exception when an application asked for neither a Cancelled code nor a resolver, so existing callers keep their contract.")]
	public async Task When_TokenIsCancelledAndNoCancellationPolicyIsSet_Then_OperationCanceledExceptionPropagates()
	{
		using var cts = new CancellationTokenSource();
		var sut = CreateApp(recorder: null);
		sut.Map("work", (CancellationToken ct) =>
		{
			cts.Cancel();
			ct.ThrowIfCancellationRequested();
			return "unreachable";
		});
		using var session = OpenSession(out _);

		var act = async () => await sut.RunAsync(["work"], cts.Token).ConfigureAwait(false);

		await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
	}

	[TestMethod]
	[Description("Regression guard: verifies a resolver alone makes cancellation observable so that the single interception point issue #81 asks for covers every final outcome, not only the ones with a table entry.")]
	public async Task When_TokenIsCancelledAndOnlyResolverIsSet_Then_ResolverSeesCancelledOutcome()
	{
		using var cts = new CancellationTokenSource();
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("work", (CancellationToken ct) =>
		{
			cts.Cancel();
			ct.ThrowIfCancellationRequested();
			return "unreachable";
		});
		using var session = OpenSession(out _);

		var exitCode = await sut.RunAsync(["work"], cts.Token).ConfigureAwait(false);

		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
		recorder.Last.Exception.Should().BeAssignableTo<OperationCanceledException>();
		exitCode.Should().Be(recorder.Last.ExitCode);
	}

	[TestMethod]
	[Description("Regression guard: verifies the exit ambient command handled in one-shot mode is classified Success so that a CI-oriented Help mapping never marks it as failed.")]
	public void When_ExitAmbientCommandRunsInOneShotMode_Then_KindIsSuccess()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Help = 3);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["exit"], out _);

		exitCode.Should().Be(0);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Success);
	}

	[TestMethod]
	[Description("Regression guard: verifies an exception thrown by a middleware is HandlerException so that pipeline failures share the handler-failure code.")]
	public void When_MiddlewareThrows_Then_KindIsHandlerException()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Use((_, _) => throw new FormatException("middleware boom"));
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["hello"], out _);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerException);
		recorder.Last.Exception!.Message.Should().Be("middleware boom");
	}

	[TestMethod]
	[Description("Regression guard: verifies a handler that cancels itself in one-shot mode is a HandlerException with a rendered message, so a mapped Cancelled code cannot make a real failure look like an operator abort.")]
	public void When_HandlerThrowsOperationCanceledWithoutCallerCancellation_Then_KindIsHandlerExceptionAndErrorIsRendered()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Cancelled = 130);
		sut.Map("boom", string () => throw new OperationCanceledException("handler gave up"));

		var exitCode = Run(sut, ["boom"], out var output);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerException);
		output.Should().Contain("handler gave up");
	}

	[TestMethod]
	[Description("Regression guard: verifies mapped cancellation returns the configured code and Kind Cancelled so that headless tools get an integer for cancellation.")]
	public async Task When_TokenIsCancelledAndCancelledIsMapped_Then_ExitCodeIs130AndKindIsCancelled()
	{
		using var cts = new CancellationTokenSource();
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Cancelled = 130);
		sut.Map("work", (CancellationToken ct) =>
		{
			cts.Cancel();
			ct.ThrowIfCancellationRequested();
			return "unreachable";
		});
		using var session = OpenSession(out _);

		var exitCode = await sut.RunAsync(["work"], cts.Token).ConfigureAwait(false);

		exitCode.Should().Be(130);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
		recorder.Last.Exception.Should().BeAssignableTo<OperationCanceledException>();
	}

	[TestMethod]
	[Description("Regression guard: verifies a token cancelled before the run is subject to the same Cancelled mapping so that early cancellation is not a special case.")]
	public async Task When_PreCancelledTokenAndCancelledIsMapped_Then_ExitCodeIs130()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync().ConfigureAwait(false);
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Cancelled = 130);
		sut.Map("work", () => "never");
		using var session = OpenSession(out _);

		var exitCode = await sut.RunAsync(["work"], cts.Token).ConfigureAwait(false);

		exitCode.Should().Be(130);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
	}

	[TestMethod]
	[Description("Regression guard: verifies the UsageError code is configurable so that applications can publish their own exit-code contract.")]
	public void When_UsageErrorIsRemapped_Then_ConfiguredCodeIsReturned()
	{
		var sut = CreateApp(recorder: null, options => options.ExitCodes.UsageError = 64);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["nope"], out _);

		exitCode.Should().Be(64);
	}

	[TestMethod]
	[Description("Regression guard: verifies the resolver receives the table-mapped code and that its return value is final.")]
	public void When_ResolverIsSet_Then_ItReceivesMappedCodeAndItsReturnWins()
	{
		var seen = new List<int>();
		var sut = CreateApp(recorder: null, options => options.ExitCodes.Resolver = outcome =>
		{
			seen.Add(outcome.ExitCode);
			return outcome.ExitCode + 10;
		});
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["nope"], out _);

		exitCode.Should().Be(12);
		seen.Should().Equal(2);
	}

	[TestMethod]
	[Description("Regression guard: verifies the resolver can override an explicit IExitResult so that one interception point governs every final outcome.")]
	public void When_ResolverSeesExitResult_Then_ItCanOverrideIt()
	{
		var sut = CreateApp(recorder: null, options => options.ExitCodes.Resolver = outcome =>
			outcome.Kind == ReplExecutionOutcomeKind.HandlerExitCode ? 99 : outcome.ExitCode);
		sut.Map("quit", () => Results.Exit(5));

		var exitCode = Run(sut, ["quit"], out _);

		exitCode.Should().Be(99);
	}

	[TestMethod]
	[Description("Regression guard: verifies a dependency the binder cannot resolve is a BindingError so that the classification of service-resolution failures is deliberate, not incidental.")]
	public void When_FromServicesDependencyIsMissing_Then_KindIsBindingError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("show", ([FromServices] IMissingDependency dependency) => dependency.ToString());

		var exitCode = Run(sut, ["show"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.BindingError);
	}

	[TestMethod]
	[Description("Regression guard: verifies a service factory that cancels during binding is a BindingError, not a HandlerException: the handler never ran, so the binding policy must still apply.")]
	public void When_ServiceFactoryCancelsDuringBinding_Then_KindIsBindingError()
	{
		var recorder = new OutcomeRecorder();
		var sut = ReplApp.Create(services =>
			services.AddSingleton<IMissingDependency>(_ => throw new OperationCanceledException("factory gave up")));
		sut.Options(options =>
		{
			options.Interactive.InteractivePolicy = InteractivePolicy.Prevent;
			options.Output.BannerEnabled = false;
			options.ExitCodes.BindingError = 9;
			options.ExitCodes.Resolver = recorder.Record;
		});
		sut.Map("show", ([FromServices] IMissingDependency dependency) => dependency.ToString());

		var exitCode = Run(sut, ["show"], out _);

		exitCode.Should().Be(9);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.BindingError);
	}

	[TestMethod]
	[Description("Regression guard: verifies a binding refusal hands the rendered result to the resolver alongside the exception, so the documented Result contract holds for every refusal and not only routing ones.")]
	public void When_BindingFails_Then_OutcomeCarriesBothTheRenderedResultAndTheException()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("set", (int value) => value);

		_ = Run(sut, ["set"], out _);

		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.BindingError);
		recorder.Last.Result.Should().BeAssignableTo<IReplResult>()
			.Which.Kind.Should().Be("validation");
		recorder.Last.Exception.Should().NotBeNull();
	}

	[TestMethod]
	[Description("Regression guard: verifies every outcome kind maps to its own configured entry so that swapping two arms of the table, or adding a kind without an entry, cannot pass unnoticed.")]
	public void When_EveryKindIsMapped_Then_EachKindReturnsItsOwnConfiguredCode()
	{
		var table = new ExitCodeOptions
		{
			Success = 10,
			Help = 11,
			UsageError = 12,
			BindingError = 13,
			HandlerError = 14,
			HandlerException = 15,
			Cancelled = 16,
			Interrupted = 17,
			FrameworkError = 18,
		};
		const int carried = 99;

		// Spelled out rather than derived from the value under test: this table IS the assertion, so a
		// reordered switch arm in ExitCodeOptions.Map has to disagree with it.
		var expectedByKind = new Dictionary<ReplExecutionOutcomeKind, int>
		{
			[ReplExecutionOutcomeKind.Success] = 10,
			[ReplExecutionOutcomeKind.Help] = 11,
			[ReplExecutionOutcomeKind.UsageError] = 12,
			[ReplExecutionOutcomeKind.BindingError] = 13,
			[ReplExecutionOutcomeKind.HandlerError] = 14,
			[ReplExecutionOutcomeKind.HandlerExitCode] = carried,
			[ReplExecutionOutcomeKind.HandlerException] = 15,
			[ReplExecutionOutcomeKind.Cancelled] = 16,
			[ReplExecutionOutcomeKind.Interrupted] = 17,
			[ReplExecutionOutcomeKind.FrameworkError] = 18,
		};

		expectedByKind.Keys.Should().BeEquivalentTo(
			Enum.GetValues<ReplExecutionOutcomeKind>(),
			"a new kind must be given an expected code here before it can ship");

		foreach (var (kind, expected) in expectedByKind)
		{
			table.Map(kind, carried).Should().Be(expected, $"{kind} must map through its own arm");
		}
	}

	[TestMethod]
	[Description("Regression guard: verifies Cancelled and Interrupted fall back to the conventional code the outcome carries when no entry is configured, so a signal bridge keeps its 128+signal convention.")]
	public void When_CancellationCodesAreUnset_Then_TheCarriedConventionalCodeIsUsed()
	{
		var table = new ExitCodeOptions();

		table.Map(ReplExecutionOutcomeKind.Cancelled, carriedExitCode: 130).Should().Be(130);
		table.Map(ReplExecutionOutcomeKind.Interrupted, carriedExitCode: 143).Should().Be(143);
		table.Map(ReplExecutionOutcomeKind.Interrupted, carriedExitCode: null).Should().Be(table.FrameworkError);
	}

	[TestMethod]
	[Description("Regression guard: verifies a handler returning an int renders it as data and exits 0 so that scalar results never become exit codes.")]
	public void When_HandlerReturnsInt_Then_ValueIsRenderedAndKindIsSuccess()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("count", () => 3);

		var exitCode = Run(sut, ["count"], out var output);

		exitCode.Should().Be(0);
		output.Should().Contain("3");
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Success);
		recorder.Last.Result.Should().Be(3);
	}

	[TestMethod]
	[Description("Regression guard: verifies the last tuple element decides the outcome so that tuple rendering follows the single-result rules.")]
	public void When_LastTupleElementIsError_Then_KindIsHandlerErrorAndResultIsLastElement()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("pair", () => ("first", Results.Error("boom", "failed")));

		var exitCode = Run(sut, ["pair"], out _);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerError);
		recorder.Last.Result.Should().BeAssignableTo<IReplResult>()
			.Which.Kind.Should().Be("error");
	}

	[TestMethod]
	[Description("Regression guard: verifies middleware can observe the handler result after next() so that cross-cutting concerns can inspect outcomes.")]
	public void When_MiddlewareObservesResultAfterNext_Then_ContextResultHoldsHandlerReturn()
	{
		object? observed = null;
		var sut = CreateApp(recorder: null);
		sut.Use(async (context, next) =>
		{
			await next().ConfigureAwait(false);
			observed = context.Result;
		});
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["hello"], out _);

		exitCode.Should().Be(0);
		observed.Should().Be("world");
	}

	[TestMethod]
	[Description("Regression guard: verifies a middleware-replaced result is rendered and classified so that middleware can transform outcomes.")]
	public void When_MiddlewareReplacesResultAfterNext_Then_ReplacementIsRenderedAndClassified()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Use(async (context, next) =>
		{
			await next().ConfigureAwait(false);
			context.Result = Results.Error("replaced", "middleware failed it");
		});
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["hello"], out var output);

		exitCode.Should().Be(1);
		output.Should().Contain("middleware failed it");
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerError);
	}

	[TestMethod]
	[Description("Regression guard: verifies a short-circuiting middleware can supply a result so that it is rendered in place of the handler's.")]
	public void When_MiddlewareShortCircuitsAndSetsResult_Then_ResultIsRendered()
	{
		var handlerCalled = false;
		var sut = CreateApp(recorder: null);
		sut.Use((context, _) =>
		{
			context.Result = "from-middleware";
			return ValueTask.CompletedTask;
		});
		sut.Map("hello", () =>
		{
			handlerCalled = true;
			return "world";
		});

		var exitCode = Run(sut, ["hello"], out var output);

		exitCode.Should().Be(0);
		handlerCalled.Should().BeFalse();
		output.Should().Contain("from-middleware");
	}

	[TestMethod]
	[Description("Regression guard: verifies a hosted protocol-passthrough refusal is a FrameworkError so that hosting-capability gaps are not reported as usage mistakes.")]
	public void When_ProtocolPassthroughIsRefusedInHostedSession_Then_KindIsFrameworkError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("mcp start", () => Results.Exit(0))
			.AsProtocolPassthrough();
		using var input = new StringReader(string.Empty);
		using var output = new StringWriter();
		var host = new InMemoryHost(input, output);

		var exitCode = sut.Run(["mcp", "start"], host);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.FrameworkError);
	}

	[TestMethod]
	[Description("Regression guard: verifies a resolver that throws degrades to the table-mapped code and reports itself once on the error stream, so a faulty exit-code hook cannot replace the run's own outcome.")]
	public void When_ResolverThrows_Then_TableCodeIsUsedAndOneDiagnosticIsWritten()
	{
		var sut = CreateApp(recorder: null, options =>
		{
			options.ExitCodes.HandlerError = 9;
			options.ExitCodes.Resolver = _ => throw new InvalidOperationException("resolver boom");
		});
		sut.Map("fail", () => Results.Error("boom", "failed"));
		using var session = OpenSplitSession(out var output, out var error);

		var exitCode = sut.Run(["fail"]);

		exitCode.Should().Be(9);
		error.ToString().Should().Contain("ExitCodes.Resolver threw InvalidOperationException", Exactly.Once());
		error.ToString().Should().Contain("resolver boom");
		output.ToString().Should().NotContain("ExitCodes.Resolver threw");
	}

	[TestMethod]
	[Description("Regression guard: verifies the resolver fallback survives a failing error stream, so reporting a resolver failure cannot itself become the failure that ends the run.")]
	public void When_ResolverThrowsAndTheErrorStreamAlsoThrows_Then_TheTableCodeIsStillReturned()
	{
		var sut = CreateApp(recorder: null, options =>
		{
			options.ExitCodes.HandlerError = 9;
			options.ExitCodes.Resolver = _ => throw new InvalidOperationException("resolver boom");
		});
		sut.Map("fail", () => Results.Error("boom", "failed"));
		using var output = new StringWriter();
		using var error = new ThrowingWriter();
		using var session = ReplSessionIO.SetSession(
			output,
			TextReader.Null,
			commandOutput: output,
			error: error,
			isHostedSession: false);

		var exitCode = sut.Run(["fail"]);

		exitCode.Should().Be(9);
	}

	[TestMethod]
	[Description("Regression guard: verifies a one-shot run reports Scope.Process so that a resolver can tell the process exit code from a per-command shell-integration mark.")]
	public void When_OneShotRunResolves_Then_ScopeIsProcess()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		_ = Run(sut, ["hello"], out _);

		recorder.Count.Should().Be(1);
		recorder.Last!.Scope.Should().Be(ReplExitCodeScope.Process);
	}

	[TestMethod]
	[Description("Regression guard: verifies an IReplResult carrying an unrecognized kind still fails so that a result the framework cannot classify never reports success to a pipeline.")]
	public void When_ResultKindIsUnrecognized_Then_KindIsHandlerErrorAndExitCodeIsOne()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("odd", () => new ReplResult("mystery", Code: null, Message: "something happened", Details: null));

		var exitCode = Run(sut, ["odd"], out _);

		exitCode.Should().Be(1);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.HandlerError);
	}

	private static ReplApp CreateApp(OutcomeRecorder? recorder, Action<ReplOptions>? configure = null)
	{
		var app = ReplApp.Create();
		app.Options(options =>
		{
			options.Interactive.InteractivePolicy = InteractivePolicy.Prevent;
			options.Output.BannerEnabled = false;
			configure?.Invoke(options);

			// After configure, so a test can observe outcomes while configure installs its own table
			// entries; a configure that sets its own Resolver keeps it.
			if (recorder is not null && options.ExitCodes.Resolver is null)
			{
				options.ExitCodes.Resolver = recorder.Record;
			}
		});
		return app;
	}

	private static int Run(ReplApp sut, string[] args, out string output)
	{
		using var session = OpenSession(out var writer);
		var exitCode = sut.Run(args);
		output = writer.ToString();
		return exitCode;
	}

	private static IDisposable OpenSession(out StringWriter writer)
	{
		writer = new StringWriter();
		return ReplSessionIO.SetSession(writer, TextReader.Null, commandOutput: writer, error: writer);
	}

	// Splits the session's error stream from its output so a framework diagnostic can be asserted on its
	// own; modelled on Given_CancelKeyHandler's session setup.
	private static IDisposable OpenSplitSession(out StringWriter output, out StringWriter error)
	{
		output = new StringWriter();
		error = new StringWriter();
		return ReplSessionIO.SetSession(
			output,
			TextReader.Null,
			commandOutput: output,
			error: error,
			isHostedSession: false);
	}

	private sealed class OutcomeRecorder
	{
		private readonly List<ReplExecutionOutcome> _observed = [];

		public ReplExecutionOutcome? Last => _observed.Count == 0 ? null : _observed[^1];

		public IReadOnlyList<ReplExecutionOutcome> Observed => _observed;

		public int Count => _observed.Count;

		public int Record(ReplExecutionOutcome outcome)
		{
			_observed.Add(outcome);
			return outcome.ExitCode;
		}
	}

	private interface IMissingDependency;

	// Stands in for a torn-down transport: every write fails.
	private sealed class ThrowingWriter : StringWriter
	{
		public override void WriteLine(string? value) => throw new ObjectDisposedException(nameof(ThrowingWriter));

		public override void Write(string? value) => throw new ObjectDisposedException(nameof(ThrowingWriter));
	}

	private sealed class InMemoryHost(TextReader input, TextWriter output) : IReplHost
	{
		public TextReader Input { get; } = input;

		public TextWriter Output { get; } = output;
	}
}
