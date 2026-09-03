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
	[Description("Regression guard: verifies an unknown command is a UsageError with exit code 2 so that it is distinguishable from a handler failure.")]
	public void When_CommandIsUnknown_Then_KindIsUsageErrorAndExitCodeIsTwo()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["nope"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError);
		recorder.Last.Result.Should().BeAssignableTo<IReplResult>();
	}

	[TestMethod]
	[Description("Regression guard: verifies an ambiguous command prefix is a UsageError so that typos never masquerade as handler errors.")]
	public void When_PrefixIsAmbiguous_Then_KindIsUsageError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("contact list", () => "list");
		sut.Map("contact load", () => "load");

		var exitCode = Run(sut, ["contact", "l"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError);
	}

	[TestMethod]
	[Description("Regression guard: verifies an unknown command option is a UsageError so that misuse and breakage return different codes.")]
	public void When_CommandOptionIsUnknown_Then_KindIsUsageError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", (string name) => name);

		var exitCode = Run(sut, ["hello", "--bogus", "x"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError);
	}

	[TestMethod]
	[Description("Regression guard: verifies an invalid global option is a UsageError so that global parse diagnostics follow the usage code.")]
	public void When_GlobalOptionIsInvalid_Then_KindIsUsageError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => "world");

		var exitCode = Run(sut, ["hello", "--output"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError);
	}

	[TestMethod]
	[Description("Regression guard: verifies an unknown output format is a UsageError so that a bad --output value is reported as misuse.")]
	public void When_OutputFormatIsUnknown_Then_KindIsUsageError()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder);
		sut.Map("hello", () => new { Name = "world" });

		var exitCode = Run(sut, ["hello", "--output:toml"], out _);

		exitCode.Should().Be(2);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.UsageError);
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
	[Description("Regression guard: verifies cancellation still propagates as an exception when Cancelled is unmapped so that existing callers keep their contract.")]
	public async Task When_TokenIsCancelledAndCancelledIsUnmapped_Then_OperationCanceledExceptionPropagates()
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

		var act = async () => await sut.RunAsync(["work"], cts.Token).ConfigureAwait(false);

		await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
		recorder.Last.Should().BeNull("the resolver must not run when cancellation is left unmapped");
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
	[Description("Regression guard: verifies a handler-thrown OperationCanceledException follows the Cancelled mapping so that prompt or self-imposed cancellations get the cancellation code.")]
	public void When_HandlerThrowsOperationCanceledAndCancelledIsMapped_Then_KindIsCancelled()
	{
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Cancelled = 130);
		sut.Map("boom", string () => throw new OperationCanceledException());

		var exitCode = Run(sut, ["boom"], out _);

		exitCode.Should().Be(130);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
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

		var exitCode = await sut.RunAsync(["work"], cts.Token);

		exitCode.Should().Be(130);
		recorder.Last!.Kind.Should().Be(ReplExecutionOutcomeKind.Cancelled);
		recorder.Last.Exception.Should().BeAssignableTo<OperationCanceledException>();
	}

	[TestMethod]
	[Description("Regression guard: verifies a token cancelled before the run is subject to the same Cancelled mapping so that early cancellation is not a special case.")]
	public async Task When_PreCancelledTokenAndCancelledIsMapped_Then_ExitCodeIs130()
	{
		using var cts = new CancellationTokenSource();
		await cts.CancelAsync();
		var recorder = new OutcomeRecorder();
		var sut = CreateApp(recorder, options => options.ExitCodes.Cancelled = 130);
		sut.Map("work", () => "never");
		using var session = OpenSession(out _);

		var exitCode = await sut.RunAsync(["work"], cts.Token);

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
	[Description("Regression guard: verifies every outcome kind has its own table entry so that a kind added later cannot silently fall through to the FrameworkError default.")]
	public void When_EveryKindIsMapped_Then_NoKindFallsThroughToTheDefaultArm()
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
			FrameworkError = 17,
		};
		const int carried = 99;

		foreach (var kind in Enum.GetValues<ReplExecutionOutcomeKind>())
		{
			var mapped = table.Map(kind, carried);
			var expected = kind switch
			{
				ReplExecutionOutcomeKind.HandlerExitCode or ReplExecutionOutcomeKind.Interrupted => carried,
				ReplExecutionOutcomeKind.FrameworkError => table.FrameworkError,
				_ => mapped,
			};
			mapped.Should().Be(expected, $"{kind} must map through its own arm");
			if (kind != ReplExecutionOutcomeKind.FrameworkError)
			{
				mapped.Should().NotBe(table.FrameworkError, $"{kind} must not fall through to the default arm");
			}
		}
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

	private static ReplApp CreateApp(OutcomeRecorder? recorder, Action<ReplOptions>? configure = null)
	{
		var app = ReplApp.Create();
		app.Options(options =>
		{
			options.Interactive.InteractivePolicy = InteractivePolicy.Prevent;
			options.Output.BannerEnabled = false;
			if (recorder is not null)
			{
				options.ExitCodes.Resolver = recorder.Record;
			}

			configure?.Invoke(options);
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

	private sealed class OutcomeRecorder
	{
		public ReplExecutionOutcome? Last { get; private set; }

		public int Record(ReplExecutionOutcome outcome)
		{
			Last = outcome;
			return outcome.ExitCode;
		}
	}

	private interface IMissingDependency;

	private sealed class InMemoryHost(TextReader input, TextWriter output) : IReplHost
	{
		public TextReader Input { get; } = input;

		public TextWriter Output { get; } = output;
	}
}
