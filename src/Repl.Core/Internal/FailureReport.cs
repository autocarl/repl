namespace Repl;

/// <summary>
/// How far the framework got in showing a failure or refusal to the caller. Reporting a failure runs
/// the same output pipeline that may have produced it, so it has three fates, not two — and the
/// classification of the run depends on which one.
/// </summary>
/// <remarks>
/// This exists because <c>RenderOutputAsync</c> answers a narrower question: its <see langword="bool"/>
/// reports whether the requested <em>format</em> was usable, and it has exactly one <c>false</c> path.
/// A renderer that throws had no representation there, so it could only be smuggled through as
/// success. Making the third fate a value of its own is what keeps a reporting failure from being
/// silently equated to a rendered one.
/// </remarks>
internal enum FailureReport
{
	/// <summary>
	/// Shown in the requested format. The caller saw the diagnostic as asked.
	/// </summary>
	Rendered,

	/// <summary>
	/// The requested output format does not exist, so nothing was shown in it — only the refusal of
	/// the format itself. A diagnostic the caller never saw cannot stand as the run's outcome, so this
	/// outranks whatever was being reported and makes the run a usage mistake.
	/// </summary>
	FormatUnknown,

	/// <summary>
	/// The renderer itself failed — an application-supplied <see cref="IOutputTransformer"/> threw —
	/// and the message was written unformatted instead. The caller did see it, so the run keeps the
	/// classification it would have had; the degradation is reported alongside it.
	/// </summary>
	Degraded,
}
