namespace Repl;

internal static class ResultFlowPager
{
	private const string MorePrompt = "--More-- Space/PageDown: continue, Enter/Down: line, Up/PageUp: back, q/Esc: stop";
	private const string EnterAlternateScreen = "\u001b[?1049h";
	private const string LeaveAlternateScreen = "\u001b[?1049l";
	private const string HideCursor = "\u001b[?25l";
	private const string ShowCursor = "\u001b[?25h";
	private const string CursorHome = "\u001b[H";
	private const string ClearToEndOfScreen = "\u001b[J";
	private const string ClearLine = "\u001b[2K";

	public static int CountLines(string payload) => SplitLines(payload).Length;

	public static async ValueTask WriteAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		CancellationToken cancellationToken = default)
	{
		await WriteAsync(
				payload,
				output,
				keyReader,
				visibleRows,
				hasMorePayload: false,
				fetchNextPayload: null,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public static async ValueTask WriteAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		ReplPagerMode pagerMode,
		bool ansiEnabled,
		CancellationToken cancellationToken = default)
	{
		await WriteAsync(
				payload,
				output,
				keyReader,
				visibleRows,
				pagerMode,
				ansiEnabled,
				hasMorePayload: false,
				fetchNextPayload: null,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public static async ValueTask WriteAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		bool hasMorePayload,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>>? fetchNextPayload,
		CancellationToken cancellationToken = default)
	{
		await WriteAsync(
				payload,
				output,
				keyReader,
				visibleRows,
				ReplPagerMode.More,
				ansiEnabled: false,
				hasMorePayload,
				fetchNextPayload,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public static async ValueTask WriteAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		ReplPagerMode pagerMode,
		bool ansiEnabled,
		bool hasMorePayload,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>>? fetchNextPayload,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(keyReader);

		if (ShouldUseScrollPager(pagerMode, ansiEnabled))
		{
			await WriteScrollAsync(
					payload,
					output,
					keyReader,
					visibleRows,
					ansiEnabled,
					hasMorePayload,
					fetchNextPayload,
					cancellationToken)
				.ConfigureAwait(false);
			return;
		}

		await WriteMoreAsync(
				payload,
				output,
				keyReader,
				visibleRows,
				hasMorePayload,
				fetchNextPayload,
				cancellationToken)
			.ConfigureAwait(false);
	}

	private static async ValueTask WriteMoreAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		bool hasMorePayload,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>>? fetchNextPayload,
		CancellationToken cancellationToken)
	{
		var state = new PagerState(SplitLines(payload), Math.Max(1, visibleRows), hasMorePayload);
		if (state.Lines.Length == 0 && !state.HasMorePayload)
		{
			return;
		}

		while (true)
		{
			if (state.Lines.Length == 0 && state.HasMorePayload && fetchNextPayload is not null)
			{
				var payloadPage = await fetchNextPayload(cancellationToken).ConfigureAwait(false);
				if (payloadPage is null)
				{
					return;
				}

				state.Reset(SplitLines(payloadPage.Payload), payloadPage.HasMore);
				continue;
			}

			if (await WriteCurrentPayloadAsync(state, output, keyReader, cancellationToken).ConfigureAwait(false))
			{
				return;
			}

			if (!state.HasMorePayload || fetchNextPayload is null)
			{
				break;
			}

			var boundaryKey = await ReadPromptAsync(output, keyReader, cancellationToken).ConfigureAwait(false);
			if (ApplyBoundaryKey(state, boundaryKey))
			{
				return;
			}

			if (state.Index < state.Lines.Length)
			{
				continue;
			}

			var nextPayload = await fetchNextPayload(cancellationToken).ConfigureAwait(false);
			if (nextPayload is null)
			{
				break;
			}

			state.Reset(SplitLines(nextPayload.Payload), nextPayload.HasMore);
		}
	}

	private static async ValueTask WriteScrollAsync(
		string payload,
		TextWriter output,
		IReplKeyReader keyReader,
		int visibleRows,
		bool ansiEnabled,
		bool hasMorePayload,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>>? fetchNextPayload,
		CancellationToken cancellationToken)
	{
		if (!ansiEnabled)
		{
			throw new InvalidOperationException("The scroll result pager requires ANSI support.");
		}

		var state = new ScrollPagerState(SplitLines(payload), Math.Max(2, visibleRows), hasMorePayload);
		if (state.Buffer.Count == 0 && !state.HasMorePayload)
		{
			return;
		}

		await output.WriteAsync(EnterAlternateScreen).ConfigureAwait(false);
		await output.WriteAsync(HideCursor).ConfigureAwait(false);
		await output.WriteAsync(CursorHome).ConfigureAwait(false);
		await output.WriteAsync(ClearToEndOfScreen).ConfigureAwait(false);
		try
		{
			await EnsureScrollBufferAsync(state, fetchNextPayload, cancellationToken).ConfigureAwait(false);
			while (true)
			{
				await RenderScrollAsync(state, output, cancellationToken).ConfigureAwait(false);
				var key = await keyReader.ReadKeyAsync(cancellationToken).ConfigureAwait(false);
				var beforeTopLine = state.TopLine;
				var action = ApplyScrollKey(state, key);
				if (action == ScrollKeyAction.Quit)
				{
					return;
				}

				if (ShouldFetchForScrollKey(state, action, beforeTopLine)
					&& state.HasMorePayload
					&& fetchNextPayload is not null)
				{
					await FetchIntoScrollBufferAsync(state, fetchNextPayload, cancellationToken).ConfigureAwait(false);
					state.TopLine = Math.Min(beforeTopLine + GetScrollDelta(action, state.ViewportHeight), state.MaxTopLine);
				}
			}
		}
		finally
		{
			await output.WriteAsync(ShowCursor).ConfigureAwait(false);
			await output.WriteAsync(LeaveAlternateScreen).ConfigureAwait(false);
			await output.FlushAsync(cancellationToken).ConfigureAwait(false);
		}
	}

	private static async ValueTask<bool> WriteCurrentPayloadAsync(
		PagerState state,
		TextWriter output,
		IReplKeyReader keyReader,
		CancellationToken cancellationToken)
	{
		while (state.Index < state.Lines.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var windowStart = state.Index;
			var take = Math.Min(state.NextWindow, state.Lines.Length - state.Index);
			for (var i = 0; i < take; i++)
			{
				await output.WriteLineAsync(state.Lines[state.Index + i]).ConfigureAwait(false);
			}

			state.Index += take;
			if (state.Index >= state.Lines.Length)
			{
				break;
			}

			var key = await ReadPromptAsync(output, keyReader, cancellationToken).ConfigureAwait(false);
			if (ApplyWindowKey(state, key, windowStart))
			{
				return true;
			}
		}

		return false;
	}

	private static bool ApplyWindowKey(PagerState state, ConsoleKeyInfo key, int windowStart)
	{
		switch (key.Key)
		{
			case ConsoleKey.Q:
			case ConsoleKey.Escape:
				return true;
			case ConsoleKey.Enter:
			case ConsoleKey.DownArrow:
				state.NextWindow = 1;
				return false;
			case ConsoleKey.UpArrow:
				state.Index = Math.Max(0, windowStart - 1);
				state.NextWindow = 1;
				return false;
			case ConsoleKey.PageUp:
				state.Index = Math.Max(0, windowStart - state.PageSize);
				state.NextWindow = state.PageSize;
				return false;
			default:
				state.NextWindow = state.PageSize;
				return false;
		}
	}

	private static bool ApplyBoundaryKey(PagerState state, ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
			case ConsoleKey.Q:
			case ConsoleKey.Escape:
				return true;
			case ConsoleKey.Enter:
			case ConsoleKey.DownArrow:
				state.NextWindow = 1;
				return false;
			case ConsoleKey.UpArrow:
				state.Index = Math.Max(0, state.Lines.Length - state.PageSize);
				state.NextWindow = state.PageSize;
				return false;
			case ConsoleKey.PageUp:
				state.Index = Math.Max(0, state.Lines.Length - state.PageSize);
				state.NextWindow = state.PageSize;
				return false;
			default:
				state.NextWindow = state.PageSize;
				return false;
		}
	}

	private static async ValueTask<ConsoleKeyInfo> ReadPromptAsync(
		TextWriter output,
		IReplKeyReader keyReader,
		CancellationToken cancellationToken)
	{
		await output.WriteAsync(MorePrompt).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);
		var key = await keyReader.ReadKeyAsync(cancellationToken).ConfigureAwait(false);
		await output.WriteLineAsync().ConfigureAwait(false);
		return key;
	}

	private static async ValueTask EnsureScrollBufferAsync(
		ScrollPagerState state,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>>? fetchNextPayload,
		CancellationToken cancellationToken)
	{
		while (state.Buffer.Count == 0 && state.HasMorePayload && fetchNextPayload is not null)
		{
			await FetchIntoScrollBufferAsync(state, fetchNextPayload, cancellationToken).ConfigureAwait(false);
		}
	}

	private static async ValueTask FetchIntoScrollBufferAsync(
		ScrollPagerState state,
		Func<CancellationToken, ValueTask<ResultFlowPagerPage?>> fetchNextPayload,
		CancellationToken cancellationToken)
	{
		var nextPayload = await fetchNextPayload(cancellationToken).ConfigureAwait(false);
		if (nextPayload is null)
		{
			state.HasMorePayload = false;
			return;
		}

		state.Append(SplitLines(nextPayload.Payload), nextPayload.HasMore);
	}

	private static async ValueTask RenderScrollAsync(
		ScrollPagerState state,
		TextWriter output,
		CancellationToken cancellationToken)
	{
		await output.WriteAsync(CursorHome).ConfigureAwait(false);
		if (state.StickyHeader is { } header)
		{
			await output.WriteAsync(ClearLine).ConfigureAwait(false);
			await output.WriteLineAsync(header).ConfigureAwait(false);
		}

		var take = Math.Min(state.ViewportHeight, Math.Max(0, state.Buffer.Count - state.TopLine));
		for (var i = 0; i < take; i++)
		{
			await output.WriteAsync(ClearLine).ConfigureAwait(false);
			await output.WriteLineAsync(state.Buffer[state.TopLine + i]).ConfigureAwait(false);
		}

		for (var i = take; i < state.ViewportHeight; i++)
		{
			await output.WriteAsync(ClearLine).ConfigureAwait(false);
			await output.WriteLineAsync().ConfigureAwait(false);
		}

		var lastLine = state.Buffer.Count == 0
			? 0
			: Math.Min(state.Buffer.Count, state.TopLine + state.ViewportHeight);
		var status = state.Buffer.Count == 0
			? "-- result-flow: loading --"
			: $"-- result-flow {state.TopLine + 1}-{lastLine}/{state.Buffer.Count}{(state.HasMorePayload ? "+" : string.Empty)}  Space: next  Up/Down: scroll  q: quit --";
		await output.WriteAsync(ClearLine).ConfigureAwait(false);
		await output.WriteAsync(status).ConfigureAwait(false);
		await output.FlushAsync(cancellationToken).ConfigureAwait(false);
	}

	private static ScrollKeyAction ApplyScrollKey(ScrollPagerState state, ConsoleKeyInfo key)
	{
		switch (key.Key)
		{
			case ConsoleKey.Q:
			case ConsoleKey.Escape:
				return ScrollKeyAction.Quit;
			case ConsoleKey.Spacebar:
			case ConsoleKey.PageDown:
			case ConsoleKey.F:
				state.TopLine = Math.Min(state.TopLine + state.ViewportHeight, state.MaxTopLine);
				return ScrollKeyAction.PageDown;
			case ConsoleKey.Enter:
			case ConsoleKey.DownArrow:
			case ConsoleKey.J:
				state.TopLine = Math.Min(state.TopLine + 1, state.MaxTopLine);
				return ScrollKeyAction.LineDown;
			case ConsoleKey.UpArrow:
			case ConsoleKey.K:
				state.TopLine = Math.Max(0, state.TopLine - 1);
				return ScrollKeyAction.Other;
			case ConsoleKey.PageUp:
			case ConsoleKey.B:
				state.TopLine = Math.Max(0, state.TopLine - state.ViewportHeight);
				return ScrollKeyAction.Other;
			case ConsoleKey.Home:
			case ConsoleKey.G when key.Modifiers.HasFlag(ConsoleModifiers.Shift):
				state.TopLine = 0;
				return ScrollKeyAction.Other;
			default:
				return ScrollKeyAction.Other;
		}
	}

	private static bool ShouldFetchForScrollKey(ScrollPagerState state, ScrollKeyAction action, int beforeTopLine) =>
		action switch
		{
			ScrollKeyAction.PageDown => state.HasReachedBottom && state.Buffer.Count > state.ViewportHeight,
			ScrollKeyAction.LineDown => beforeTopLine == state.TopLine && state.HasReachedBottom,
			_ => false,
		};

	private static int GetScrollDelta(ScrollKeyAction action, int viewportHeight) =>
		action == ScrollKeyAction.PageDown ? viewportHeight : 1;

	private static bool ShouldUseScrollPager(ReplPagerMode pagerMode, bool ansiEnabled) =>
		ansiEnabled && pagerMode is ReplPagerMode.Auto or ReplPagerMode.Scroll;

	private static string[] SplitLines(string payload) =>
		string.IsNullOrEmpty(payload)
			? []
			: SplitNonEmptyPayloadLines(payload);

	private static string[] SplitNonEmptyPayloadLines(string payload)
	{
		var lines = new List<string>();
		foreach (var line in payload.AsSpan().EnumerateLines())
		{
			lines.Add(line.ToString());
		}

		// EnumerateLines adds a trailing empty entry when the payload ends with a newline;
		// strip it to stay consistent with how the pager counts visible lines.
		if (lines.Count > 0 && lines[^1].Length == 0)
		{
			lines.RemoveAt(lines.Count - 1);
		}

		return [.. lines];
	}

	private sealed class PagerState(string[] lines, int pageSize, bool hasMorePayload)
	{
		private string[] _lines = lines;

		public string[] Lines => _lines;

		public int PageSize { get; } = pageSize;

		public int NextWindow { get; set; } = pageSize;

		public int Index { get; set; }

		public bool HasMorePayload { get; private set; } = hasMorePayload;

		public void Reset(string[] lines, bool hasMorePayload)
		{
			_lines = lines;
			Index = 0;
			HasMorePayload = hasMorePayload;
		}
	}

	private enum ScrollKeyAction
	{
		Other,
		LineDown,
		PageDown,
		Quit,
	}

	private sealed class ScrollPagerState
	{
		public ScrollPagerState(string[] lines, int visibleRows, bool hasMorePayload)
		{
			StickyHeader = TryGetStickyHeader(lines);
			Buffer = [.. GetContentLines(lines, StickyHeader)];
			ViewportHeight = Math.Max(1, visibleRows - (StickyHeader is null ? 1 : 2));
			HasMorePayload = hasMorePayload;
		}

		public List<string> Buffer { get; }

		public string? StickyHeader { get; }

		public int ViewportHeight { get; }

		public int TopLine { get; set; }

		public bool HasMorePayload { get; set; }

		public int MaxTopLine => Math.Max(0, Buffer.Count - ViewportHeight);

		public bool HasReachedBottom => TopLine >= MaxTopLine;

		public void Append(string[] lines, bool hasMorePayload)
		{
			Buffer.AddRange(GetContentLines(lines, StickyHeader));
			HasMorePayload = hasMorePayload;
		}

		private static string? TryGetStickyHeader(string[] lines) =>
			lines.Length > 1 && lines[0].Contains("\u001b[1m", StringComparison.Ordinal)
				? lines[0]
				: null;

		private static IEnumerable<string> GetContentLines(string[] lines, string? stickyHeader) =>
			stickyHeader is not null && lines.Length > 0 && string.Equals(lines[0], stickyHeader, StringComparison.Ordinal)
				? lines.Skip(1)
				: lines;
	}
}
