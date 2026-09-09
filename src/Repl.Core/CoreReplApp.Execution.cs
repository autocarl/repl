using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Repl;

public sealed partial class CoreReplApp : ISubInvocableReplApp
{
	/// <summary>
	/// Runs the app in synchronous mode.
	/// </summary>
	/// <param name="args">Command-line arguments.</param>
	/// <returns>Process exit code.</returns>
	public int Run(string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
#pragma warning disable VSTHRD002 // Sync API intentionally blocks to preserve a conventional Run(...) entrypoint.
		return RunAsync(args).AsTask().GetAwaiter().GetResult();
#pragma warning restore VSTHRD002
	}

	/// <summary>
	/// Runs the app in asynchronous mode.
	/// </summary>
	/// <param name="args">Command-line arguments.</param>
	/// <param name="cancellationToken">Cancellation token.</param>
	/// <returns>Process exit code.</returns>
	public ValueTask<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(args);
		_ = _description;
		_ = _commands.Count;
		_ = _middleware.Count;
		_ = _options;
		return ExecuteCoreAsync(args, _services, cancellationToken: cancellationToken);
	}

	internal ValueTask<int> RunWithServicesAsync(
		string[] args,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default) =>
		ExecuteCoreAsync(args, serviceProvider, cancellationToken: cancellationToken);

	/// <summary>
	/// Executes a nested command invocation that preserves the session baseline.
	/// Used by MCP tool calls where the global options from the initial session
	/// must remain in effect even though the sub-invocation tokens don't contain them.
	/// </summary>
	internal ValueTask<int> RunSubInvocationAsync(
		string[] args,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default) =>
		ExecuteCoreAsync(args, serviceProvider, isSubInvocation: true, cancellationToken);

	ValueTask<int> ISubInvocableReplApp.RunSubInvocationAsync(
		string[] args,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken) =>
		RunSubInvocationAsync(args, serviceProvider, cancellationToken);

	private async ValueTask<int> ExecuteCoreAsync(
		IReadOnlyList<string> args,
		IServiceProvider serviceProvider,
		bool isSubInvocation = false,
		CancellationToken cancellationToken = default)
	{
		// Resolved outside the cancellation guard: ExitCodes.Resolver is application code, and a
		// resolver that throws OperationCanceledException must not be mistaken for a cancelled run.
		var outcome = await ExecuteCoreOutcomeWithCancellationPolicyAsync(
				args,
				serviceProvider,
				isSubInvocation,
				cancellationToken)
			.ConfigureAwait(false);
		return ResolveExitCode(outcome, isSubInvocation);
	}

	/// <summary>
	/// Runs the pipeline and reports the outcome without resolving an exit code, so a host wrapper that
	/// has its own teardown to run can resolve once, at the end, instead of once per stage.
	/// </summary>
	internal ValueTask<ExecutionOutcome> RunOutcomeWithServicesAsync(
		string[] args,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken = default) =>
		ExecuteCoreOutcomeWithCancellationPolicyAsync(args, serviceProvider, isSubInvocation: false, cancellationToken);

	/// <summary>
	/// Converts an already-cancelled caller token into a <see cref="ReplExecutionOutcomeKind.Cancelled"/>
	/// outcome, so a host wrapper can apply the policy before doing any work of its own. Returns
	/// <see langword="null"/> when the token is not cancelled, and throws when the application configured
	/// no way to observe cancellation — the same contract the pipeline itself follows.
	/// </summary>
	internal ExecutionOutcome? TryObserveCallerCancellation(CancellationToken cancellationToken)
	{
		if (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}

		if (!IsConvertibleCancellation(isSubInvocation: false, cancellationToken))
		{
			cancellationToken.ThrowIfCancellationRequested();
		}

		return ExecutionOutcome.Cancelled(new OperationCanceledException(cancellationToken));
	}

	private async ValueTask<ExecutionOutcome> ExecuteCoreOutcomeWithCancellationPolicyAsync(
		IReadOnlyList<string> args,
		IServiceProvider serviceProvider,
		bool isSubInvocation,
		CancellationToken cancellationToken)
	{
		_options.Interaction.SetObserver(observer: ExecutionObserver);
		try
		{
			try
			{
				// Inside the try so a token cancelled before the run follows the same Cancelled policy.
				cancellationToken.ThrowIfCancellationRequested();
				return await ExecuteCoreOutcomeAsync(args, serviceProvider, isSubInvocation, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (OperationCanceledException ex) when (IsConvertibleCancellation(isSubInvocation, cancellationToken))
			{
				return ExecutionOutcome.Cancelled(ex);
			}
		}
		finally
		{
			_options.Interaction.SetObserver(observer: null);
		}
	}

	/// <summary>
	/// Whether an <see cref="OperationCanceledException"/> leaving the pipeline should become a
	/// <see cref="ReplExecutionOutcomeKind.Cancelled"/> outcome rather than propagate. Only the caller's
	/// own token counts — a handler that cancels itself is a failure, classified where it is rendered —
	/// and an application must have asked for cancellation to be observable, through either the table
	/// entry or the resolver.
	/// </summary>
	private bool IsConvertibleCancellation(bool isSubInvocation, CancellationToken cancellationToken) =>
		!isSubInvocation
		&& cancellationToken.IsCancellationRequested
		&& (_options.ExitCodes.Cancelled is not null || _options.ExitCodes.Resolver is not null);

	private async ValueTask<ExecutionOutcome> ExecuteCoreOutcomeAsync(
		IReadOnlyList<string> args,
		IServiceProvider serviceProvider,
		bool isSubInvocation,
		CancellationToken cancellationToken)
	{
		if (ReplSessionIO.IsProgrammatic && !ReplSessionIO.HasCurrentProgrammaticInvocationContract)
		{
			var contractFailure = Results.Validation(
				"The programmatic invocation adapter is incompatible with this Repl.Core version. "
				+ "Update Repl.Mcp to the same package version.");
			_ = await RenderOutputAsync(contractFailure, requestedFormat: null, cancellationToken)
				.ConfigureAwait(false);
			return ExecutionOutcome.FrameworkError(contractFailure);
		}

		var globalOptions = GlobalOptionParser.Parse(args, _options.Output, _options.Parsing);
		if (await TryHandleGlobalDiagnosticsAsync(globalOptions, cancellationToken).ConfigureAwait(false) is { } globalDiagnostics)
		{
			return globalDiagnostics;
		}

		return await ExecuteParsedCoreAsync(globalOptions, serviceProvider, isSubInvocation, cancellationToken)
			.ConfigureAwait(false);
	}

	/// <summary>
	/// Applies the exit-code policy to the process exit code of a run, once. Sub-invocations (nested MCP
	/// tool calls) keep the built-in defaults and skip <see cref="ExitCodeOptions.Resolver"/>: the policy
	/// describes the process exit, and nested callers only test for non-zero.
	/// </summary>
	internal int ResolveExitCode(ExecutionOutcome outcome, bool isSubInvocation) =>
		isSubInvocation
			? ExitCodeOptions.MapDefault(outcome.Kind, outcome.ExplicitExitCode)
			: ResolveConfiguredExitCode(outcome, ReplExitCodeScope.Process);

	/// <summary>
	/// Applies the exit-code policy to the code decorating one interactive command's shell-integration
	/// command-end mark. Always uses the configured table: a mark describes that command, not the process,
	/// so the sub-invocation shortcut of <see cref="ResolveExitCode"/> does not apply. Callers must first
	/// check that a mark will actually carry the code — the resolver is application code and must not run
	/// for a mark nobody writes.
	/// </summary>
	internal int ResolveCommandEndExitCode(ExecutionOutcome outcome) =>
		ResolveConfiguredExitCode(outcome, ReplExitCodeScope.ShellIntegrationMark);

	private int ResolveConfiguredExitCode(ExecutionOutcome outcome, ReplExitCodeScope scope)
	{
		var exitCode = _options.ExitCodes.Map(outcome.Kind, outcome.ExplicitExitCode);
		if (_options.ExitCodes.Resolver is { } resolver)
		{
			exitCode = InvokeResolver(resolver, outcome, exitCode, scope);
		}

		if (scope == ReplExitCodeScope.Process)
		{
			ExecutionObserver?.OnOutcome(outcome.Kind, exitCode);
		}

		return exitCode;
	}

	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "A faulty exit-code resolver must not replace the run's own outcome; it degrades to the table-mapped code.")]
	private static int InvokeResolver(
		Func<ReplExecutionOutcome, int> resolver,
		ExecutionOutcome outcome,
		int mappedExitCode,
		ReplExitCodeScope scope)
	{
		try
		{
			return resolver(
				new ReplExecutionOutcome(outcome.Kind, mappedExitCode, outcome.Result, outcome.Exception) { Scope = scope });
		}
		catch (Exception ex)
		{
			// The resolver is application code on the way out of a run; a throw here would replace a real
			// outcome with an unrelated failure, so it degrades to the table code and says so once.
			TryWriteResolverDiagnostic(ex, mappedExitCode);
			return mappedExitCode;
		}
	}

	[SuppressMessage(
		"Design",
		"CA1031:Do not catch general exception types",
		Justification = "Reporting a resolver failure must not itself fail the run: the error stream may be disposed or its transport already torn down.")]
	private static void TryWriteResolverDiagnostic(Exception resolverFailure, int mappedExitCode)
	{
		try
		{
#pragma warning disable MA0045 // Intentionally synchronous — the exit-code policy resolves on non-async members
			ReplSessionIO.Error.WriteLine(
				$"Error: ExitCodes.Resolver threw {resolverFailure.GetType().Name} ({resolverFailure.Message}); using exit code {mappedExitCode.ToString(CultureInfo.InvariantCulture)}.");
#pragma warning restore MA0045
		}
		catch
		{
			// Best-effort: the fallback exit code is the contract, the diagnostic is a courtesy.
		}
	}

	private async ValueTask<ExecutionOutcome> ExecuteParsedCoreAsync(
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		bool isSubInvocation,
		CancellationToken cancellationToken)
	{
			_globalOptionsSnapshot.Update(globalOptions.CustomGlobalNamedOptions); // volatile ref swap — safe under concurrent sub-invocations
			if (!isSubInvocation)
			{
				_globalOptionsSnapshot.SetSessionBaseline();
			}
			using var runtimeStateScope = PushRuntimeState(serviceProvider, isInteractiveSession: false);
			var prefixResolution = ResolveUniquePrefixes(globalOptions.RemainingTokens);
			var resolvedGlobalOptions = globalOptions with { RemainingTokens = prefixResolution.Tokens };
			var ambiguousOutcome = await TryHandleAmbiguousPrefixAsync(
						prefixResolution,
						globalOptions,
						resolvedGlobalOptions,
						serviceProvider,
						cancellationToken)
					.ConfigureAwait(false);
			if (ambiguousOutcome is not null) return ambiguousOutcome.Value;

			var preResolvedRouteResolution = TryPreResolveRouteForBanner(resolvedGlobalOptions);
			if (!ShouldSuppressGlobalBanner(resolvedGlobalOptions, preResolvedRouteResolution?.Match))
			{
				await TryRenderBannerAsync(resolvedGlobalOptions, serviceProvider, cancellationToken).ConfigureAwait(false);
			}

			var preExecutionOutcome = await TryHandlePreExecutionAsync(
						resolvedGlobalOptions,
						serviceProvider,
						cancellationToken)
					.ConfigureAwait(false);
			if (preExecutionOutcome is not null) return preExecutionOutcome.Value;

			var resolution = preResolvedRouteResolution
				?? ResolveWithDiagnostics(resolvedGlobalOptions.RemainingTokens);
			var match = resolution.Match;
			if (match is null)
			{
				return await TryHandleContextDeeplinkAsync(
							resolvedGlobalOptions,
							serviceProvider,
							cancellationToken,
							constraintFailure: resolution.ConstraintFailure,
							missingArgumentsFailure: resolution.MissingArgumentsFailure)
						.ConfigureAwait(false);
			}

			return await ExecuteMatchedCommandAndMaybeEnterInteractiveAsync(
					match,
					resolvedGlobalOptions,
					serviceProvider,
					cancellationToken)
				.ConfigureAwait(false);
	}

	private async ValueTask<ExecutionOutcome?> TryHandleAmbiguousPrefixAsync(
		PrefixResolutionResult prefixResolution,
		GlobalInvocationOptions globalOptions,
		GlobalInvocationOptions resolvedGlobalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (!prefixResolution.IsAmbiguous)
		{
			return null;
		}

		if (!ShouldSuppressGlobalBanner(resolvedGlobalOptions, preResolvedMatch: null))
		{
			await TryRenderBannerAsync(resolvedGlobalOptions, serviceProvider, cancellationToken).ConfigureAwait(false);
		}

		var ambiguous = CreateAmbiguousPrefixResult(prefixResolution);
		_ = await RenderOutputAsync(ambiguous, globalOptions.OutputFormat, cancellationToken)
			.ConfigureAwait(false);
		return ExecutionOutcome.Usage(ambiguous);
	}

	private static bool ShouldSuppressGlobalBanner(
		GlobalInvocationOptions globalOptions,
		RouteMatch? preResolvedMatch)
	{
		if (globalOptions.HelpRequested || globalOptions.RemainingTokens.Count == 0)
		{
			return false;
		}

		return preResolvedMatch?.Route.Command.IsProtocolPassthrough == true;
	}

	private RouteResolver.RouteResolutionResult? TryPreResolveRouteForBanner(GlobalInvocationOptions globalOptions)
	{
		if (globalOptions.HelpRequested || globalOptions.RemainingTokens.Count == 0)
		{
			return null;
		}

		return ResolveWithDiagnostics(globalOptions.RemainingTokens);
	}

	private async ValueTask<ExecutionOutcome?> TryHandlePreExecutionAsync(
		GlobalInvocationOptions options,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var completionHandled = await TryHandleCompletionCommandAsync(options, serviceProvider, cancellationToken)
			.ConfigureAwait(false);
		if (completionHandled is not null)
		{
			return completionHandled.Value;
		}

		if (options.HelpRequested)
		{
			var rendered = await RenderHelpAsync(options, cancellationToken).ConfigureAwait(false);
			return rendered ? ExecutionOutcome.Help : ExecutionOutcome.Usage();
		}

		if (options.RemainingTokens.Count == 0)
		{
			return await HandleEmptyInvocationAsync(options, serviceProvider, cancellationToken)
				.ConfigureAwait(false);
		}

		return await TryHandleAmbientInNonInteractiveAsync(options, serviceProvider, cancellationToken)
			.ConfigureAwait(false);
	}

	private async ValueTask<ExecutionOutcome> ExecuteMatchedCommandAndMaybeEnterInteractiveAsync(
		RouteMatch match,
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (match.Route.Command.IsProtocolPassthrough)
		{
			return await ExecuteProtocolPassthroughCommandAsync(match, globalOptions, serviceProvider, cancellationToken)
				.ConfigureAwait(false);
		}

		var (outcome, enterInteractive) = await ExecuteMatchedCommandAsync(
				match,
				globalOptions,
				serviceProvider,
				scopeTokens: null,
				cancellationToken)
			.ConfigureAwait(false);

		if (enterInteractive || (outcome.IsSuccessLike && ShouldEnterInteractive(globalOptions, allowAuto: false)))
		{
			var matchedPathLength = globalOptions.RemainingTokens.Count - match.RemainingTokens.Count;
			var matchedPathTokens = globalOptions.RemainingTokens.Take(matchedPathLength).ToArray();
			var interactiveScope = GetDeepestContextScopePath(matchedPathTokens);
			return await RunInteractiveSessionAsync(interactiveScope, serviceProvider, cancellationToken).ConfigureAwait(false);
		}

		return outcome;
	}

	/// <summary>
	/// Executes a protocol-passthrough command under the single passthrough contract shared
	/// by the CLI one-shot and interactive paths: the hosted-capability guard, the
	/// <see cref="ReplSessionIO.PushProtocolPassthrough"/> scope handlers can observe, and —
	/// outside hosted sessions — stdout/stderr isolation (framework output on stderr, the
	/// handler payload alone on stdout).
	/// </summary>
	internal async ValueTask<ExecutionOutcome> ExecuteProtocolPassthroughCommandAsync(
		RouteMatch match,
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (ReplSessionIO.IsHostedSession && !match.Route.Command.SupportsHostedProtocolPassthrough)
		{
			var refusal = Results.Error(
				"protocol_passthrough_hosted_not_supported",
				$"Command '{match.Route.Template.Template}' is protocol passthrough and requires a handler parameter of type IReplIoContext in hosted sessions.");
			_ = await RenderOutputAsync(refusal, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return ExecutionOutcome.FrameworkError(refusal);
		}

		using var protocolPassthroughScope = ReplSessionIO.PushProtocolPassthrough();

		if (ReplSessionIO.IsSessionActive)
		{
			var (sessionOutcome, _) = await ExecuteMatchedCommandAsync(
					match,
					globalOptions,
					serviceProvider,
					scopeTokens: null,
					cancellationToken)
				.ConfigureAwait(false);
			return sessionOutcome;
		}

		using var protocolScope = ReplSessionIO.SetSession(
			Console.Error,
			Console.In,
			ansiMode: AnsiMode.Never,
			commandOutput: Console.Out,
			error: Console.Error,
			isHostedSession: false);
		var (outcome, _) = await ExecuteMatchedCommandAsync(
				match,
				globalOptions,
				serviceProvider,
				scopeTokens: null,
				cancellationToken)
			.ConfigureAwait(false);
		return outcome;
	}

	private async ValueTask<ExecutionOutcome> HandleEmptyInvocationAsync(
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (ShouldEnterInteractive(globalOptions, allowAuto: true))
		{
			return await RunInteractiveSessionAsync([], serviceProvider, cancellationToken).ConfigureAwait(false);
		}

		var helpText = BuildHumanHelp([]);
		await ReplSessionIO.Output.WriteLineAsync(helpText).ConfigureAwait(false);
		return ExecutionOutcome.Help;
	}

	private async ValueTask<ExecutionOutcome?> TryHandleCompletionCommandAsync(
		GlobalInvocationOptions options,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (options.RemainingTokens.Count == 0
			|| !string.Equals(options.RemainingTokens[0], InteractiveSession.CompleteAmbientToken, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var completed = await HandleCompletionAmbientCommandAsync(
				commandTokens: options.RemainingTokens.Skip(1).ToArray(),
				scopeTokens: [],
				serviceProvider: serviceProvider,
				cancellationToken: cancellationToken)
			.ConfigureAwait(false);
		return completed ? ExecutionOutcome.Success : ExecutionOutcome.Usage();
	}

	private async ValueTask<ExecutionOutcome?> TryHandleAmbientInNonInteractiveAsync(
		GlobalInvocationOptions options,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (options.RemainingTokens.Count != 1)
		{
			return null;
		}

		var token = options.RemainingTokens[0];
		AmbientCommandOutcome ambientOutcome;
		if (string.Equals(token, InteractiveSession.ExitAmbientToken, StringComparison.OrdinalIgnoreCase))
		{
			ambientOutcome = await HandleExitAmbientCommandAsync().ConfigureAwait(false);
		}
		else if (string.Equals(token, InteractiveSession.UpAmbientToken, StringComparison.Ordinal))
		{
			ambientOutcome = await HandleUpAmbientCommandAsync(scopeTokens: [], isInteractiveSession: false)
				.ConfigureAwait(false);
		}
		else
		{
			return null;
		}

		return ambientOutcome switch
		{
			AmbientCommandOutcome.Exit or AmbientCommandOutcome.Handled => ExecutionOutcome.Success,
			AmbientCommandOutcome.HandledError => ExecutionOutcome.Usage(),
			_ => null,
		};
	}

	private async ValueTask TryRenderBannerAsync(
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		if (globalOptions.LogoSuppressed)
		{
			_allBannersSuppressed.Value = true;
		}

		if (_bannerRendered.Value || _allBannersSuppressed.Value || !_options.Output.BannerEnabled)
		{
			return;
		}

		var requestedFormat = string.IsNullOrWhiteSpace(globalOptions.OutputFormat)
			? _options.Output.DefaultFormat
			: globalOptions.OutputFormat;
		if (!_options.Output.BannerFormats.Contains(requestedFormat))
		{
			return;
		}

		var banner = BuildBannerText();
		if (!string.IsNullOrWhiteSpace(banner))
		{
			await ReplSessionIO.Output.WriteLineAsync(banner).ConfigureAwait(false);
		}

		if (_banner is not null)
		{
			await InvokeBannerAsync(_banner, serviceProvider, cancellationToken).ConfigureAwait(false);
		}

		_bannerRendered.Value = true;
	}

	private async ValueTask<ExecutionOutcome> TryHandleContextDeeplinkAsync(
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken,
		RouteResolver.RouteConstraintFailure? constraintFailure = null,
		RouteResolver.RouteMissingArgumentsFailure? missingArgumentsFailure = null)
	{
		var activeGraph = ResolveActiveRoutingGraph();
		var contextMatch = ContextResolver.ResolveExact(activeGraph.Contexts, globalOptions.RemainingTokens, _options.Parsing);
		if (contextMatch is null)
		{
			var failure = CreateRouteResolutionFailureResult(
				tokens: globalOptions.RemainingTokens,
				constraintFailure,
				missingArgumentsFailure);
			_ = await RenderOutputAsync(failure, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return ExecutionOutcome.Usage(failure);
		}

		var contextValidation = await ValidateContextAsync(contextMatch, serviceProvider, cancellationToken)
			.ConfigureAwait(false);
		if (!contextValidation.IsValid)
		{
			_ = await RenderOutputAsync(
					contextValidation.Failure,
					globalOptions.OutputFormat,
					cancellationToken)
				.ConfigureAwait(false);
			return ExecutionOutcome.Usage(contextValidation.Failure);
		}

		if (!ShouldEnterInteractive(globalOptions, allowAuto: true))
		{
			var helpText = BuildHumanHelp(globalOptions.RemainingTokens);
			await ReplSessionIO.Output.WriteLineAsync(helpText).ConfigureAwait(false);
			return ExecutionOutcome.Help;
		}

		return await RunInteractiveSessionAsync(globalOptions.RemainingTokens.ToArray(), serviceProvider, cancellationToken)
			.ConfigureAwait(false);
	}

	[SuppressMessage(
		"Maintainability",
		"MA0051:Method is too long",
		Justification = "Execution path intentionally keeps validation, binding, middleware and rendering in one place.")]
	internal async ValueTask<(ExecutionOutcome Outcome, bool EnterInteractive)> ExecuteMatchedCommandAsync(
		RouteMatch match,
		GlobalInvocationOptions globalOptions,
		IServiceProvider serviceProvider,
		List<string>? scopeTokens,
		CancellationToken cancellationToken)
	{
		var activeGraph = ResolveActiveRoutingGraph();
		_options.Interaction.SetPrefilledAnswers(globalOptions.PromptAnswers);
		var commandParsingOptions = BuildEffectiveCommandParsingOptions(globalOptions.GlobalOptionConfiguration.CaseSensitivity);
		var optionComparer = commandParsingOptions.OptionCaseSensitivity == ReplCaseSensitivity.CaseInsensitive
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal;
		var knownOptionNames = new HashSet<string>(match.Route.OptionSchema.Parameters.Keys, optionComparer);
		if (TryFindGlobalCommandOptionCollision(globalOptions, knownOptionNames, out var collidingOption))
		{
			var collision = Results.Validation($"Ambiguous option '{collidingOption}'. It is defined as both global and command option.");
			_ = await RenderOutputAsync(collision, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return (ExecutionOutcome.Usage(collision), false);
		}

		var parsedOptions = InvocationOptionParser.Parse(
			match.RemainingTokens,
			match.Route.OptionSchema,
			commandParsingOptions,
			globalOptions.CustomGlobalTokenOwnership);
		if (parsedOptions.HasErrors)
		{
			var firstError = parsedOptions.Diagnostics
				.First(diagnostic => diagnostic.Severity == ParseDiagnosticSeverity.Error);
			var optionFailure = Results.Validation(firstError.Message);
			_ = await RenderOutputAsync(optionFailure, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return (ExecutionOutcome.Usage(optionFailure), false);
		}
		var matchedPathLength = globalOptions.RemainingTokens.Count - match.RemainingTokens.Count;
		var matchedPathTokens = globalOptions.RemainingTokens.Take(matchedPathLength).ToArray();
		var bindingContext = CreateInvocationBindingContext(
			match,
			parsedOptions,
			globalOptions,
			commandParsingOptions,
			matchedPathTokens,
			activeGraph.Contexts,
			serviceProvider,
			cancellationToken);
		// Binding and the handler share one try so progress cleanup and rendering stay uniform; the flag
		// tells a binder exception (InvalidOperationException, conversion FormatException, …) apart from
		// anything thrown after binding — the handler, middleware, user validators, banners, transformers.
		var bound = false;
		try
		{
			var arguments = HandlerArgumentBinder.Bind(match.Route.Command.Handler, bindingContext);
			bound = true;
			var contextFailure = await ValidateContextsForMatchAsync(
					match,
					matchedPathTokens,
					activeGraph.Contexts,
					serviceProvider,
					cancellationToken)
				.ConfigureAwait(false);
			if (contextFailure is not null)
			{
				_ = await RenderOutputAsync(contextFailure, globalOptions.OutputFormat, cancellationToken)
					.ConfigureAwait(false);
				return (ExecutionOutcome.Usage(contextFailure), false);
			}

			await TryRenderCommandBannerAsync(match.Route.Command, globalOptions.OutputFormat, serviceProvider, cancellationToken)
				.ConfigureAwait(false);
				var result = await ExecuteWithMiddlewareAsync(
						match.Route.Command.Handler,
						arguments,
						serviceProvider,
						cancellationToken)
					.ConfigureAwait(false);
				await TryClearProgressAsync(serviceProvider).ConfigureAwait(false);

				if (TupleDecomposer.IsTupleResult(result, out var tuple))
				{
					return await RenderTupleResultAsync(tuple, scopeTokens, globalOptions, cancellationToken)
						.ConfigureAwait(false);
				}

				if (result is EnterInteractiveResult enterInteractive)
				{
					if (enterInteractive.Payload is not null)
					{
						_ = await RenderOutputAsync(
								enterInteractive.Payload,
								globalOptions.OutputFormat,
								cancellationToken,
								scopeTokens is not null,
								globalOptions.ResultFlow)
							.ConfigureAwait(false);
					}

					return (ExecutionOutcome.Success, true);
				}

				var normalizedResult = ApplyNavigationResult(result, scopeTokens);
				ExecutionObserver?.OnResult(normalizedResult);
				var rendered = await RenderOutputAsync(
						normalizedResult,
						globalOptions.OutputFormat,
						cancellationToken,
						scopeTokens is not null,
						globalOptions.ResultFlow)
					.ConfigureAwait(false);
				// RenderOutputAsync returns false only for an unknown requested output format: a usage mistake.
				return (rendered ? ClassifyResult(normalizedResult) : ExecutionOutcome.Usage(normalizedResult), false);
		}
		// The ambient runtime state, not scopeTokens: a protocol-passthrough command always passes
		// scopeTokens: null, interactive or not, so it is not a mode discriminator.
		catch (OperationCanceledException ex) when (!IsInteractiveSession && !cancellationToken.IsCancellationRequested)
		{
			// One-shot, and nobody asked for this run to stop: the handler cancelled itself, which is a
			// failure like any other exception. Rendering it is the point — a bare rethrow here used to
			// exit silently, leaving a caller who mapped ExitCodes.Cancelled unable to tell a real
			// failure from an operator abort. The interactive loop keeps its own Ctrl+C semantics.
			await TryClearProgressAsync(serviceProvider).ConfigureAwait(false);
			var cancellationFailure = Results.Error("execution_error", ex.Message);
			_ = await RenderOutputAsync(cancellationFailure, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			// A service factory can cancel before the handler ever runs, so this is a binding failure
			// unless binding already completed.
			return (bound
				? ExecutionOutcome.HandlerException(ex)
				: ExecutionOutcome.Binding(ex, cancellationFailure), false);
		}
		catch (OperationCanceledException)
		{
			await TryClearProgressAsync(serviceProvider).ConfigureAwait(false);
			throw;
		}
		catch (InvalidOperationException ex)
		{
			await TryClearProgressAsync(serviceProvider).ConfigureAwait(false);
			var validationFailure = Results.Validation(ex.Message);
			_ = await RenderOutputAsync(validationFailure, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return (bound
				? ExecutionOutcome.HandlerException(ex)
				: ExecutionOutcome.Binding(ex, validationFailure), false);
		}
		catch (Exception ex)
		{
			await TryClearProgressAsync(serviceProvider).ConfigureAwait(false);
			var unwrapped = ex is TargetInvocationException { InnerException: { } inner } ? inner : ex;
			var executionFailure = Results.Error("execution_error", unwrapped.Message);
			_ = await RenderOutputAsync(executionFailure, globalOptions.OutputFormat, cancellationToken)
				.ConfigureAwait(false);
			return (bound
				? ExecutionOutcome.HandlerException(unwrapped)
				: ExecutionOutcome.Binding(unwrapped, executionFailure), false);
		}
	}

	private static async ValueTask TryClearProgressAsync(IServiceProvider serviceProvider)
	{
		if (serviceProvider.GetService(typeof(IReplInteractionChannel)) is not IReplInteractionChannel interaction)
		{
			return;
		}

		try
		{
			await interaction.ClearProgressAsync().ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			// Clearing progress is best-effort cleanup and may race with cancellation.
		}
		catch (ObjectDisposedException)
		{
			// Clearing progress is best-effort cleanup and may happen after the channel is disposed.
		}
		catch (InvalidOperationException)
		{
			// Clearing progress is best-effort cleanup and may race with teardown.
		}
	}

	private async ValueTask<(ExecutionOutcome Outcome, bool EnterInteractive)> RenderTupleResultAsync(
		ITuple tuple,
		List<string>? scopeTokens,
		GlobalInvocationOptions globalOptions,
		CancellationToken cancellationToken)
	{
		var isInteractive = scopeTokens is not null;
		var outcome = ExecutionOutcome.Success;
		var enterInteractive = false;

		for (var i = 0; i < tuple.Length; i++)
		{
			var element = tuple[i];

			// EnterInteractiveResult: extract payload (if any) and signal interactive entry.
			if (element is EnterInteractiveResult eir)
			{
				enterInteractive = true;
				element = eir.Payload;
				if (element is null)
				{
					continue;
				}
			}

			var isLast = i == tuple.Length - 1;

			// Navigation results: only apply navigation on the last element.
			var normalized = element is ReplNavigationResult nav && !isLast
				? nav.Payload
				: isLast
					? ApplyNavigationResult(element, scopeTokens)
					: element;

			ExecutionObserver?.OnResult(normalized);

			var rendered = await RenderOutputAsync(
					normalized,
					globalOptions.OutputFormat,
					cancellationToken,
					isInteractive,
					globalOptions.ResultFlow)
				.ConfigureAwait(false);

			if (!rendered)
			{
				return (ExecutionOutcome.Usage(normalized), false);
			}

			if (isLast)
			{
				outcome = ClassifyResult(normalized);
			}
		}

		return (outcome, enterInteractive);
	}

	/// <summary>
	/// Classifies a rendered handler result. Anything that is not an <see cref="IReplResult"/> — including a
	/// bare <see cref="int"/> — is data and therefore a success; only <see cref="IExitResult"/> carries a code.
	/// </summary>
	private static ExecutionOutcome ClassifyResult(object? result)
	{
		if (result is IExitResult exitResult)
		{
			return ExecutionOutcome.Exit(exitResult);
		}

		if (result is not IReplResult replResult)
		{
			return result is null ? ExecutionOutcome.Success : new ExecutionOutcome(ReplExecutionOutcomeKind.Success, result);
		}

		var kind = replResult.Kind.ToLowerInvariant();
		return kind is "text" or "success"
			? new ExecutionOutcome(ReplExecutionOutcomeKind.Success, replResult)
			: ExecutionOutcome.HandlerError(replResult);
	}

	internal async ValueTask<bool> RenderOutputAsync(
		object? result,
		string? requestedFormat,
		CancellationToken cancellationToken,
		bool isInteractive = false,
		ResultFlowInvocationOptions? resultFlow = null)
	{
		if (result is IExitResult exitResult)
		{
			if (exitResult.Payload is null)
			{
				return true;
			}

			result = exitResult.Payload;
		}

		var format = string.IsNullOrWhiteSpace(requestedFormat)
			? _options.Output.DefaultFormat
			: requestedFormat;
		if (!_options.Output.Transformers.TryGetValue(format, out var transformer))
		{
			// Unknown format is a user-facing validation issue; avoid silent failures from exception swallowing.
			await ReplSessionIO.Output.WriteLineAsync($"Error: unknown output format '{format}'.").ConfigureAwait(false);
			return false;
		}

		if (result is IReplPageSource pageSource)
		{
			return await RenderPageSourceAsync(
				pageSource,
				transformer,
				isInteractive,
				resultFlow,
				cancellationToken)
				.ConfigureAwait(false);
		}

		var payload = await transformer.TransformAsync(result, cancellationToken).ConfigureAwait(false);
		payload = TryColorizeStructuredPayload(payload, format, isInteractive);
		if (!string.IsNullOrEmpty(payload))
		{
			await WritePayloadAsync(payload, transformer, resultFlow, cancellationToken).ConfigureAwait(false);
		}

		return true;
	}

	private async ValueTask<bool> RenderPageSourceAsync(
		IReplPageSource source,
		IOutputTransformer transformer,
		bool isInteractive,
		ResultFlowInvocationOptions? resultFlow,
		CancellationToken cancellationToken)
	{
		var request = CreatePageSourceRequest(resultFlow);
		var page = await FetchPageSourceAsync(source, request, cancellationToken).ConfigureAwait(false);
		var payload = await transformer.TransformAsync(page, cancellationToken).ConfigureAwait(false);
		payload = TryColorizeStructuredPayload(payload, transformer.Name, isInteractive);

		if (!TryCreatePager(
				payload,
				transformer,
				resultFlow,
				page.PageInfo.HasMore,
				out var keyReader,
				out var visibleRows,
				out var pagerMode,
				out var ansiEnabled))
		{
			return await WritePageSourcePayloadAsync(payload).ConfigureAwait(false);
		}

		return await RenderPageSourcePagerAsync(
				source,
				transformer,
				isInteractive,
				request,
				page,
				keyReader,
				visibleRows,
				pagerMode,
				ansiEnabled,
				cancellationToken)
			.ConfigureAwait(false);
	}

	private static async ValueTask<bool> WritePageSourcePayloadAsync(string payload)
	{
		if (!string.IsNullOrEmpty(payload))
		{
			await ReplSessionIO.Output.WriteLineAsync(payload).ConfigureAwait(false);
		}

		return true;
	}

	private async ValueTask<bool> RenderPageSourcePagerAsync(
		IReplPageSource source,
		IOutputTransformer transformer,
		bool isInteractive,
		ReplPageRequest request,
		IReplPage page,
		IReplKeyReader keyReader,
		int visibleRows,
		ReplPagerMode pagerMode,
		bool ansiEnabled,
		CancellationToken cancellationToken)
	{
		var nextCursor = page.PageInfo.NextCursor;
		var pagerPayload = await TransformPagerPageAsync(transformer, page, ResultFlowPageRenderMode.Initial, cancellationToken)
			.ConfigureAwait(false);
		pagerPayload = TryColorizeStructuredPayload(pagerPayload, transformer.Name, isInteractive);
		await ResultFlowPager.WriteAsync(
				pagerPayload,
				ReplSessionIO.Output,
				keyReader,
				new ResultFlowPagerOptions
				{
					VisibleRows = visibleRows,
					VisibleRowsProvider = ResolvePagerVisibleRows,
					PagerMode = pagerMode,
					AnsiEnabled = ansiEnabled,
					HasMorePayload = page.PageInfo.HasMore,
					FetchNextPayload = FetchNextPayloadAsync,
					PagerRenderers = _options.Output.ResultFlow.PagerRenderers,
					MaxBufferedLines = _options.Output.ResultFlow.MaxBufferedLines,
				},
				cancellationToken)
			.ConfigureAwait(false);
		return true;

		async ValueTask<ResultFlowPagerPage?> FetchNextPayloadAsync(CancellationToken token)
		{
			if (string.IsNullOrWhiteSpace(nextCursor))
			{
				return null;
			}

			var nextRequest = request with { Cursor = nextCursor };
			var nextPage = await FetchPageSourceAsync(source, nextRequest, token).ConfigureAwait(false);
			nextCursor = nextPage.PageInfo.NextCursor;
			var nextPayload = await TransformPagerPageAsync(transformer, nextPage, ResultFlowPageRenderMode.Continuation, token)
				.ConfigureAwait(false);
			nextPayload = TryColorizeStructuredPayload(nextPayload, transformer.Name, isInteractive);
			return new ResultFlowPagerPage(
				nextPayload,
				nextPage.PageInfo.HasMore,
				ContainsPresentationChrome: false);
		}
	}

	private async ValueTask<ExecutionOutcome?> TryHandleGlobalDiagnosticsAsync(
		GlobalInvocationOptions globalOptions,
		CancellationToken cancellationToken)
	{
		if (!globalOptions.HasErrors)
		{
			return null;
		}

		var firstError = globalOptions.Diagnostics
			.First(diagnostic => diagnostic.Severity == ParseDiagnosticSeverity.Error);
		var globalFailure = Results.Validation(firstError.Message);
		_ = await RenderOutputAsync(globalFailure, globalOptions.OutputFormat, cancellationToken)
			.ConfigureAwait(false);
		return ExecutionOutcome.Usage(globalFailure);
	}

	private static ValueTask<string> TransformPagerPageAsync(
		IOutputTransformer transformer,
		IReplPage page,
		ResultFlowPageRenderMode mode,
		CancellationToken cancellationToken)
	{
		var displayPage = CreatePagerDisplayPage(page);
		return transformer is IResultFlowOutputTransformer resultFlowTransformer
			? resultFlowTransformer.TransformPageAsync(displayPage, mode, cancellationToken)
			: transformer.TransformAsync(displayPage, cancellationToken);
	}

	private async ValueTask WritePayloadAsync(
		string payload,
		IOutputTransformer transformer,
		ResultFlowInvocationOptions? resultFlow,
		CancellationToken cancellationToken)
	{
		if (TryCreatePager(
				payload,
				transformer,
				resultFlow,
				out var keyReader,
				out var visibleRows,
				out var pagerMode,
				out var ansiEnabled))
		{
			await ResultFlowPager.WriteAsync(
					payload,
					ReplSessionIO.Output,
					keyReader,
					new ResultFlowPagerOptions
					{
						VisibleRows = visibleRows,
						PagerMode = pagerMode,
						AnsiEnabled = ansiEnabled,
						PagerRenderers = _options.Output.ResultFlow.PagerRenderers,
						MaxBufferedLines = _options.Output.ResultFlow.MaxBufferedLines,
					},
					cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		await ReplSessionIO.Output.WriteLineAsync(payload).ConfigureAwait(false);
	}

	private bool TryCreatePager(
		string payload,
		IOutputTransformer transformer,
		ResultFlowInvocationOptions? resultFlow,
		[NotNullWhen(true)] out IReplKeyReader? keyReader,
		out int visibleRows,
		out ReplPagerMode pagerMode,
		out bool ansiEnabled)
		=> TryCreatePager(
			payload,
			transformer,
			resultFlow,
			hasMorePayload: false,
			out keyReader,
			out visibleRows,
			out pagerMode,
			out ansiEnabled);

	private bool TryCreatePager(
		string payload,
		IOutputTransformer transformer,
		ResultFlowInvocationOptions? resultFlow,
		bool hasMorePayload,
		[NotNullWhen(true)] out IReplKeyReader? keyReader,
		out int visibleRows,
		out ReplPagerMode pagerMode,
		out bool ansiEnabled)
	{
		keyReader = null;
		visibleRows = 0;
		ansiEnabled = false;

		pagerMode = resultFlow?.PagerMode ?? _options.Output.ResultFlow.DefaultPagerMode;
		if (pagerMode == ReplPagerMode.Off
			|| ReplSessionIO.IsProgrammatic
			|| ReplSessionIO.IsProtocolPassthrough
			|| !transformer.SupportsInteractivePaging)
		{
			return false;
		}

		if (!TryResolvePagerVisibleRows(out visibleRows)
			|| (!hasMorePayload && ResultFlowPager.CountLines(payload) <= visibleRows)
			|| !TryResolvePagerKeyReader(out keyReader))
		{
			return false;
		}

		ansiEnabled = _options.Output.IsAnsiEnabled();
		return true;
	}

	private bool TryResolvePagerVisibleRows(out int visibleRows)
	{
		visibleRows = ResolvePagerVisibleRows();
		return visibleRows > 0;
	}

	private int ResolvePagerVisibleRows()
	{
		var height = ReplSessionIO.WindowSize?.Height ?? TryGetConsoleWindowHeight();
		var reservedRows = Math.Max(0, _options.Output.ResultFlow.ReservedVisibleRows);
		return height is > 0
			? Math.Max(1, height.Value - reservedRows)
			: Math.Max(1, _options.Output.ResultFlow.DefaultPageSize);
	}

	private static bool TryResolvePagerKeyReader([NotNullWhen(true)] out IReplKeyReader? keyReader)
	{
		if (ReplSessionIO.KeyReader is { } sessionKeyReader)
		{
			keyReader = sessionKeyReader;
			return true;
		}

		if (!Console.IsInputRedirected && !Console.IsOutputRedirected && !ReplSessionIO.IsSessionActive)
		{
			keyReader = new ConsoleKeyReader();
			return true;
		}

		keyReader = null;
		return false;
	}

	private ReplPageRequest CreatePageSourceRequest(ResultFlowInvocationOptions? resultFlow)
	{
		var surface = ResolveResultSurface();
		return new ReplPagingContext(
				_options.Output.ResultFlow,
				resultFlow ?? new ResultFlowInvocationOptions(),
				surface,
				ResolveVisibleRowCapacityHint(surface))
			.CreateRequest();
	}

	private static IReplPage CreatePagerDisplayPage(IReplPage page)
	{
		if (!page.PageInfo.HasMore)
		{
			return page;
		}

		var pageInfo = page.PageInfo with
		{
			NextCursor = null,
		};
		return new ReplPageDisplaySnapshot(page, pageInfo);
	}

	private async ValueTask<IReplPage> FetchPageSourceAsync(
		IReplPageSource source,
		ReplPageRequest request,
		CancellationToken cancellationToken)
	{
		var diagnostics = ResolveResultFlowDiagnostics();
		diagnostics?.OnDiagnostic(new ReplResultFlowDiagnostic(
			ReplResultFlowDiagnosticKind.PageFetchStarting,
			request.Cursor,
			request.PageSize));

		try
		{
			var page = await source.FetchPageAsync(request, cancellationToken).ConfigureAwait(false);
			diagnostics?.OnDiagnostic(new ReplResultFlowDiagnostic(
				ReplResultFlowDiagnosticKind.PageFetchSucceeded,
				request.Cursor,
				request.PageSize,
				page.UntypedItems.Count));
			return page;
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception ex)
		{
			diagnostics?.OnDiagnostic(new ReplResultFlowDiagnostic(
				ReplResultFlowDiagnosticKind.PageFetchFailed,
				request.Cursor,
				request.PageSize,
				Exception: ex));
			throw;
		}
	}

	private IReplResultFlowDiagnostics? ResolveResultFlowDiagnostics()
	{
		var serviceProvider = _runtimeState.Value?.ServiceProvider ?? _services;
		return serviceProvider.GetService(typeof(IReplResultFlowDiagnostics)) as IReplResultFlowDiagnostics
			?? _services.GetService(typeof(IReplResultFlowDiagnostics)) as IReplResultFlowDiagnostics;
	}

	private string TryColorizeStructuredPayload(string payload, string format, bool isInteractive)
	{
		if (string.IsNullOrEmpty(payload)
			|| !isInteractive
			|| !_options.Output.ColorizeStructuredInteractive
			|| !_options.Output.IsAnsiEnabled()
			|| !string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
		{
			return payload;
		}

		return JsonAnsiColorizer.Colorize(payload, _options.Output.ResolvePalette());
	}

	internal async ValueTask<bool> RenderHelpAsync(
		GlobalInvocationOptions globalOptions,
		CancellationToken cancellationToken)
	{
		var activeGraph = ResolveActiveRoutingGraph();
		var discoverableRoutes = ResolveDiscoverableRoutes(
			activeGraph.Routes,
			activeGraph.Contexts,
			globalOptions.RemainingTokens,
			StringComparison.OrdinalIgnoreCase);
		var discoverableContexts = ResolveDiscoverableContexts(
			activeGraph.Contexts,
			globalOptions.RemainingTokens,
			StringComparison.OrdinalIgnoreCase);
		var requestedFormat = string.IsNullOrWhiteSpace(globalOptions.OutputFormat)
			? _options.Output.DefaultFormat
			: globalOptions.OutputFormat;
		if (string.Equals(requestedFormat, "human", StringComparison.OrdinalIgnoreCase))
		{
			var helpText = BuildHumanHelp(globalOptions.RemainingTokens);
			await ReplSessionIO.Output.WriteLineAsync(helpText).ConfigureAwait(false);
			return true;
		}

		if (_options.Output.TryBuildHelpOutput(
				requestedFormat,
				discoverableRoutes,
				discoverableContexts,
				globalOptions.RemainingTokens,
				_options.Parsing,
				CurrentServiceProvider,
				_options.AmbientCommands,
				out var customHelpOutput))
		{
			return await RenderOutputAsync(customHelpOutput, requestedFormat, cancellationToken).ConfigureAwait(false);
		}

		var machineHelp = HelpTextBuilder.BuildModel(
			discoverableRoutes,
			discoverableContexts,
			globalOptions.RemainingTokens,
			_options.Parsing,
			CurrentServiceProvider);
		return await RenderOutputAsync(machineHelp, requestedFormat, cancellationToken).ConfigureAwait(false);
	}

	private async ValueTask<object?> ExecuteWithMiddlewareAsync(
		Delegate handler,
		object?[] arguments,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var context = new ReplExecutionContext(serviceProvider, cancellationToken);
		var index = -1;

		async ValueTask NextAsync()
		{
			index++;
			if (index == _middleware.Count)
			{
				// Stored on the context so middleware can observe or replace it after awaiting next().
				context.Result = await CommandInvoker
					.InvokeAsync(handler, arguments)
					.ConfigureAwait(false);
				return;
			}

			var middleware = _middleware[index];
			await middleware(context, NextAsync).ConfigureAwait(false);
		}

		await NextAsync().ConfigureAwait(false);
		return context.Result;
	}

	private static object? ApplyNavigationResult(object? result, List<string>? scopeTokens)
	{
		if (result is not ReplNavigationResult navigation)
		{
			return result;
		}

		if (scopeTokens is null)
		{
			return navigation.Payload;
		}

		ApplyNavigation(scopeTokens, navigation);
		return navigation.Payload;
	}

	private static void ApplyNavigation(List<string> scopeTokens, ReplNavigationResult navigation)
	{
		if (navigation.Kind == ReplNavigationKind.Up)
		{
			if (scopeTokens.Count > 0)
			{
				scopeTokens.RemoveAt(scopeTokens.Count - 1);
			}

			return;
		}

		if (!string.IsNullOrWhiteSpace(navigation.TargetPath))
		{
			scopeTokens.Clear();
			scopeTokens.AddRange(
				navigation.TargetPath
					.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
		}
	}

	private InvocationBindingContext CreateInvocationBindingContext(
		RouteMatch match,
		OptionParsingResult parsedOptions,
		GlobalInvocationOptions globalOptions,
		ParsingOptions commandParsingOptions,
		string[] matchedPathTokens,
		IReadOnlyList<ContextDefinition> contexts,
		IServiceProvider serviceProvider,
		CancellationToken cancellationToken)
	{
		var contextValues = BuildContextHierarchyValues(match.Route.Template, matchedPathTokens, contexts);
		contextValues.Add(CreatePagingContext(globalOptions));
		var mergedNamedOptions = MergeNamedOptions(
			parsedOptions.NamedOptions,
			globalOptions.CustomGlobalNamedOptions);
		return new InvocationBindingContext(
			match.Values,
			mergedNamedOptions,
			parsedOptions.PositionalArguments,
			match.Route.OptionSchema,
			commandParsingOptions.OptionCaseSensitivity,
			contextValues,
			_options.Parsing.NumericFormatProvider,
			serviceProvider,
			_options.Interaction,
			_implicitServiceParameters,
			cancellationToken);
	}

	private ReplPagingContext CreatePagingContext(GlobalInvocationOptions globalOptions)
	{
		var surface = ResolveResultSurface();
		var visibleRows = ResolveVisibleRowCapacityHint(surface);
		return new ReplPagingContext(
			_options.Output.ResultFlow,
			globalOptions.ResultFlow,
			surface,
			visibleRows);
	}

	private ReplResultSurface ResolveResultSurface()
	{
		if (ReplSessionIO.IsProgrammatic)
		{
			return ReplResultSurface.Programmatic;
		}

		if (_runtimeState.Value?.IsInteractiveSession == true)
		{
			return ReplResultSurface.Interactive;
		}

		if (ReplSessionIO.IsHostedSession)
		{
			return ReplResultSurface.Hosted;
		}

		return Console.IsOutputRedirected
			? ReplResultSurface.Redirected
			: ReplResultSurface.Console;
	}

	private int? ResolveVisibleRowCapacityHint(ReplResultSurface surface)
	{
		if (surface is ReplResultSurface.Redirected or ReplResultSurface.Programmatic)
		{
			return null;
		}

		var height = ReplSessionIO.WindowSize?.Height ?? TryGetConsoleWindowHeight();
		if (height is not > 0)
		{
			return null;
		}

		var reservedRows = Math.Max(0, _options.Output.ResultFlow.ReservedVisibleRows);
		return Math.Max(1, height.Value - reservedRows);
	}

	private static int? TryGetConsoleWindowHeight()
	{
		try
		{
			var height = Console.WindowHeight;
			return height > 0 ? height : null;
		}
		catch (IOException)
		{
			return null;
		}
		catch (PlatformNotSupportedException)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		catch (System.Security.SecurityException)
		{
			return null;
		}
	}

	private static bool TryFindGlobalCommandOptionCollision(
		GlobalInvocationOptions globalOptions,
		HashSet<string> knownOptionNames,
		out string collidingOption)
	{
		foreach (var globalOption in globalOptions.CustomGlobalNamedOptions.Keys)
		{
			if (!knownOptionNames.Contains(globalOption))
			{
				continue;
			}

			collidingOption = $"--{globalOption}";
			return true;
		}

		collidingOption = string.Empty;
		return false;
	}

	private static IReadOnlyDictionary<string, IReadOnlyList<string>> MergeNamedOptions(
		IReadOnlyDictionary<string, IReadOnlyList<string>> commandNamedOptions,
		IReadOnlyDictionary<string, IReadOnlyList<string>> globalNamedOptions)
	{
		if (globalNamedOptions.Count == 0)
		{
			return commandNamedOptions;
		}

		var merged = new Dictionary<string, IReadOnlyList<string>>(
			commandNamedOptions,
			StringComparer.OrdinalIgnoreCase);
		foreach (var pair in globalNamedOptions)
		{
			if (merged.TryGetValue(pair.Key, out var existing))
			{
				var appended = existing.Concat(pair.Value).ToArray();
				merged[pair.Key] = appended;
				continue;
			}

			merged[pair.Key] = pair.Value;
		}

		return merged;
	}

	private ParsingOptions BuildEffectiveCommandParsingOptions(ReplCaseSensitivity optionCaseSensitivity)
	{
		var isInteractiveSession = _runtimeState.Value?.IsInteractiveSession == true;
		return new ParsingOptions
		{
			AllowUnknownOptions = _options.Parsing.AllowUnknownOptions,
			OptionCaseSensitivity = optionCaseSensitivity,
			// Structured/programmatic callers already provide argument boundaries. Expanding an
			// MCP-supplied @file token here would replace one allowed value with arbitrary CLI
			// tokens from the server filesystem, bypassing the adapter's schema allow-list.
			AllowResponseFiles = !isInteractiveSession
				&& !ReplSessionIO.IsProgrammatic
				&& _options.Parsing.AllowResponseFiles,
		};
	}
}
