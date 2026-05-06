using Repl.Tests.TerminalSupport;

namespace Repl.Tests;

[TestClass]
public sealed class Given_ResultFlowPager
{
	[TestMethod]
	[Description("Result-flow pager advances by page on Space and stops on Q.")]
	public async Task When_PagingWithSpaceAndQuit_Then_WritesOnlyRequestedPages()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour\nfive",
			writer,
			keys,
			visibleRows: 2,
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("two");
		output.Should().Contain("three");
		output.Should().Contain("four");
		output.Should().NotContain("five");
		output.Should().Contain("--More--");
		output.Should().Contain("Space/PageDown: continue");
		output.Should().Contain("Enter/Down: line");
		output.Should().Contain("Up/PageUp: back");
		output.Should().Contain("q/Esc: stop");
	}

	[TestMethod]
	[Description("Result-flow pager advances by one line on Enter.")]
	public async Task When_PagingWithEnter_Then_AdvancesSingleLine()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Enter, '\r'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 2,
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("two");
		output.Should().Contain("three");
		output.Should().NotContain("four");
	}

	[TestMethod]
	[Description("Result-flow pager UpArrow moves back one line instead of jumping to the header.")]
	public async Task When_PagingBackWithUpArrow_Then_DoesNotRepeatHeader()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.UpArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"# At Area Event Summary\nr1\nr2\nr3\nr4\nr5",
			writer,
			keys,
			visibleRows: 2,
			CancellationToken.None);

		var output = writer.ToString();
		output.Split("# At Area Event Summary", StringSplitOptions.None)
			.Should().HaveCount(2);
		output.Should().Contain("r1");
		output.Should().Contain("r2");
		output.Should().Contain("r3");
	}

	[TestMethod]
	[Description("Result-flow pager fetches the next data page in the same interactive run.")]
	public async Task When_CurrentPayloadEndsAndMoreDataExists_Then_SpaceFetchesNextPayload()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo",
			writer,
			keys,
			visibleRows: 2,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("three\nfour", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(1);
		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("two");
		output.Should().Contain("three");
		output.Should().Contain("four");
	}

	[TestMethod]
	[Description("Result-flow pager stops at a data-page boundary without fetching more data when the user quits.")]
	public async Task When_CurrentPayloadEndsAndUserQuits_Then_DoesNotFetchNextPayload()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo",
			writer,
			keys,
			visibleRows: 2,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("three\nfour", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(0);
		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("two");
		output.Should().NotContain("three");
		output.Should().NotContain("four");
	}

	[TestMethod]
	[Description("Result-flow pager fetches the next data page instead of showing an empty --More-- prompt when a payload has no content.")]
	public async Task When_CurrentPayloadIsEmptyAndMoreDataExists_Then_FetchesNextPayload()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader([]);

		await ResultFlowPager.WriteAsync(
			string.Empty,
			writer,
			keys,
			visibleRows: 2,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("one\ntwo", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(1);
		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("two");
		output.Should().NotContain("--More--");
	}

	[TestMethod]
	[Description("Result-flow pager replays the previous full window when the user presses UpArrow at a data-page boundary.")]
	public async Task When_AtPayloadBoundaryAndUserPressesUpArrow_Then_ReplaysPreviousWindow()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.UpArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 2,
			hasMorePayload: true,
			fetchNextPayload: _ => throw new InvalidOperationException("Should not fetch while replaying the previous window."),
			CancellationToken.None);

		var output = writer.ToString();
		output.Split("three", StringSplitOptions.None).Should().HaveCount(3);
		output.Split("four", StringSplitOptions.None).Should().HaveCount(3);
	}

	[TestMethod]
	[Description("Result-flow scroll pager owns an alternate-screen viewport instead of relying on terminal scrollback.")]
	public async Task When_ScrollPagerRunsWithAnsi_Then_UsesAlternateScreenViewport()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("\u001b[?1049h");
		output.Should().Contain("\u001b[?1049l");
		output.Should().Contain("\u001b[H\u001b[J");
		output.Should().Contain("one");
		output.Should().Contain("three");
		output.Should().Contain("q: quit");
		output.Should().NotContain("--More--");
	}

	[TestMethod]
	[Description("Result-flow scroll pager fetches additional payloads into the same viewport when the user pages past the buffered end.")]
	public async Task When_ScrollPagerReachesBufferedEnd_Then_FetchesNextPayload()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("four\nfive", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(1);
		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("four");
		output.Should().Contain("\u001b[?1049h");
	}

	[TestMethod]
	[Description("Result-flow scroll pager advances to the new buffered end when a fetch returns fewer lines than one viewport.")]
	public async Task When_ScrollPagerFetchesShortPayload_Then_ViewportAdvances()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 4,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ => ValueTask.FromResult<ResultFlowPagerPage?>(
				new ResultFlowPagerPage("five", HasMore: false)),
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("3-5/5");
		output.Should().Contain("five");
	}

	[TestMethod]
	[Description("Result-flow scroll pager does not fetch another payload when the current payload is exactly visible and the user presses Space once.")]
	public async Task When_ScrollPagerContentExactlyFitsViewport_Then_SpaceDoesNotFetchImmediately()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree",
			writer,
			keys,
			visibleRows: 4,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("four", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(0);
		writer.ToString().Should().NotContain("four");
	}

	[TestMethod]
	[Description("Result-flow pager does not add a phantom empty line when a payload ends with a newline.")]
	public async Task When_PayloadEndsWithNewline_Then_LineCountExcludesTrailingEmptyLine()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\n",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			CancellationToken.None);

		writer.ToString().Should().Contain("1-2/2");
	}

	[TestMethod]
	[Description("Result-flow scroll pager treats unrecognized keys as no-ops and does not advance the viewport or trigger a fetch.")]
	public async Task When_ScrollPagerUnknownKeyPressed_Then_ViewportDoesNotAdvanceAndNoFetch()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.F1, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("five", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(0);
		writer.ToString().Should().NotContain("five");
	}

	[TestMethod]
	[Description("Result-flow scroll pager advances the viewport only on Space/PageDown, not on Enter or other keys.")]
	public async Task When_ScrollPagerEnterKeyPressed_Then_ViewportAdvancesByOneLine()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Enter, '\r'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			CancellationToken.None);

		// Enter maps to DownArrow (one line); status bar should show 2-3/4, not 3-4/4
		writer.ToString().Should().Contain("2-3/4");
	}

	[TestMethod]
	[Description("Result-flow scroll pager advances by one line when Down fetches the next payload at a boundary.")]
	public async Task When_ScrollPagerDownFetchesNextPayload_Then_ViewportAdvancesOneLine()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ => ValueTask.FromResult<ResultFlowPagerPage?>(
				new ResultFlowPagerPage("four\nfive", HasMore: false)),
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("2-3/3+");
		output.Should().Contain("3-4/5");
		output.Should().NotContain("4-5/5");
	}

	[TestMethod]
	[Description("Result-flow scroll pager keeps a rich table header pinned and skips duplicate headers from later payloads.")]
	public async Task When_ScrollPagerHasRichTableHeader_Then_HeaderStaysPinned()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Spacebar, ' '),
			MakeKey(ConsoleKey.Q, 'q'),
		]);
		var header = "\u001b[1m#\u001b[0m  \u001b[1mAt\u001b[0m";

		await ResultFlowPager.WriteAsync(
			$"{header}\none\ntwo\nthree",
			writer,
			keys,
			visibleRows: 4,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ => ValueTask.FromResult<ResultFlowPagerPage?>(
				new ResultFlowPagerPage($"{header}\nfour\nfive", HasMore: false)),
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain($"{header}\r\nthree\r\nfour");
		output.Should().Contain("3-4/5");
	}

	[TestMethod]
	[Description("Result-flow scroll pager does not clear the whole viewport on every redraw.")]
	public async Task When_ScrollPagerRedraws_Then_DoesNotClearScreenEveryTime()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			CancellationToken.None);

		writer.ToString().Split("\u001b[J").Length.Should().Be(2);
		writer.ToString().Should().NotContain("\u001b[2K");
	}

	[TestMethod]
	[Description("Result-flow scroll pager strips page footer hints already represented by its own status bar.")]
	public async Task When_ScrollPagerReceivesPageFooterLines_Then_FooterLinesAreNotRendered()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nShowing 2 of 5. Next data page: rerun with --result:cursor page-2.",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ => ValueTask.FromResult<ResultFlowPagerPage?>(
				new ResultFlowPagerPage("three\nShowing 1 of 5.", HasMore: false)),
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("one");
		output.Should().Contain("three");
		output.Should().NotContain("Showing 2 of 5");
		output.Should().NotContain("Showing 1 of 5");
	}

	[TestMethod]
	[Description("Result-flow scroll pager skips duplicate rich table headers even when they are not the first line in a fetched payload.")]
	public async Task When_ScrollPagerReceivesIndentedDuplicateHeader_Then_HeaderIsNotBuffered()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);
		var header = "\u001b[1m#\u001b[0m   \u001b[1mAt\u001b[0m";

		await ResultFlowPager.WriteAsync(
			$"{header}\none\ntwo\nShowing 2 of 5.",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ => ValueTask.FromResult<ResultFlowPagerPage?>(
				new ResultFlowPagerPage($"Showing 1 of 5.\n{header}\nthree", HasMore: false)),
			CancellationToken.None);

		writer.ToString().Should().Contain($"{header}\r\nthree");
		writer.ToString().Split(header).Length.Should().Be(4);
	}

	[TestMethod]
	[Description("Result-flow scroll pager End moves to the end of the currently buffered content.")]
	public async Task When_ScrollPagerEndPressed_Then_MovesToKnownEndWithoutFetching()
	{
		using var writer = new StringWriter();
		var fetches = 0;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.End, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour\nfive",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: true,
			fetchNextPayload: _ =>
			{
				fetches++;
				return ValueTask.FromResult<ResultFlowPagerPage?>(
					new ResultFlowPagerPage("six", HasMore: false));
			},
			CancellationToken.None);

		fetches.Should().Be(0);
		writer.ToString().Should().Contain("4-5/5+");
	}

	[TestMethod]
	[Description("Result-flow scroll pager recalculates viewport height between redraws.")]
	public async Task When_ScrollPagerHeightChanges_Then_ViewportUsesCurrentHeight()
	{
		using var writer = new StringWriter();
		var visibleRows = 5;
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.DownArrow, '\0'),
			MakeKey(ConsoleKey.Q, 'q'),
		]);
		var reads = 0;

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree\nfour\nfive",
			writer,
			keys,
			visibleRows,
			visibleRowsProvider: () => reads++ == 0 ? visibleRows : 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			hasMorePayload: false,
			fetchNextPayload: null,
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("1-4/5");
		output.Should().Contain("2-3/5");
		output.Should().Contain("\u001b[H\u001b[J");
	}

	[TestMethod]
	[Description("Result-flow scroll pager disables terminal line wrapping while the alternate screen is active.")]
	public async Task When_ScrollPagerRuns_Then_LineWrappingIsDisabledDuringAlternateScreen()
	{
		using var writer = new StringWriter();
		var keys = new FakeKeyReader(
		[
			MakeKey(ConsoleKey.Q, 'q'),
		]);

		await ResultFlowPager.WriteAsync(
			"one\ntwo\nthree",
			writer,
			keys,
			visibleRows: 3,
			pagerMode: ReplPagerMode.Scroll,
			ansiEnabled: true,
			CancellationToken.None);

		var output = writer.ToString();
		output.Should().Contain("\u001b[?7l");
		output.Should().Contain("\u001b[?7h");
		output.IndexOf("\u001b[?7l", StringComparison.Ordinal)
			.Should().BeLessThan(output.IndexOf("\u001b[?7h", StringComparison.Ordinal));
	}

	private static ConsoleKeyInfo MakeKey(ConsoleKey key, char keyChar) =>
		new(keyChar, key, shift: false, alt: false, control: false);
}
