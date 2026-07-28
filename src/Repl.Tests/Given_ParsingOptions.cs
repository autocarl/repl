namespace Repl.Tests;

[TestClass]
public sealed class Given_ParsingOptions
{
	[TestMethod]
	[Description("Changing the comparer atomically publishes a new ownership snapshot while a previously returned snapshot remains immutable.")]
	public void When_CaseSensitivityChanges_Then_OwnershipSnapshotsRemainSelfConsistent()
	{
		var sut = new ParsingOptions();
		sut.AddGlobalOption<string>("tenant", aliases: ["-T"]);
		var sensitive = sut.CaptureGlobalOptionConfiguration();

		sut.OptionCaseSensitivity = ReplCaseSensitivity.CaseInsensitive;
		var insensitive = sut.CaptureGlobalOptionConfiguration();

		sensitive.CaseSensitivity.Should().Be(ReplCaseSensitivity.CaseSensitive);
		sensitive.Ownership.ContainsKey("-t").Should().BeFalse();
		insensitive.CaseSensitivity.Should().Be(ReplCaseSensitivity.CaseInsensitive);
		insensitive.Ownership.Should().ContainKey("-t");
		insensitive.Ownership["-t"].Name.Should().Be("tenant");
		ReferenceEquals(sensitive, insensitive).Should().BeFalse();
	}

	[TestMethod]
	[Description("Concurrent global-option writers publish complete snapshots without lost registrations or a mutable dictionary escaping to readers.")]
	public async Task When_GlobalOptionsAreRegisteredConcurrently_Then_AllRegistrationsArePublished()
	{
		const int optionCount = 128;
		var sut = new ParsingOptions();
		var initialSnapshot = sut.CaptureGlobalOptionConfiguration();
		using var start = new ManualResetEventSlim(initialState: false);
		using var readerReady = new ManualResetEventSlim(initialState: false);
		var writer = Task.Run(() =>
		{
			start.Wait();
			Parallel.For(0, optionCount, index =>
				sut.AddGlobalOption<string>($"option-{index}", aliases: [$"-x{index}"]));
		});
		var observedSnapshots = 0;
		var reader = Task.Run(() =>
		{
			readerReady.Set();
			start.Wait();
			do
			{
				initialSnapshot.Definitions.Should().BeEmpty();
				initialSnapshot.Ownership.Should().BeEmpty();
				var snapshot = sut.CaptureGlobalOptionConfiguration();
				foreach (var definition in snapshot.Definitions.Values)
				{
					snapshot.Ownership[definition.CanonicalToken].Should().BeSameAs(definition);
					foreach (var alias in definition.Aliases)
					{
						snapshot.Ownership[alias].Should().BeSameAs(definition);
					}
				}

				Interlocked.Increment(location: ref observedSnapshots);
				Thread.Yield();
			}
			while (!writer.IsCompleted);
		});

		readerReady.Wait();
		start.Set();
		await writer.ConfigureAwait(false);
		await reader.ConfigureAwait(false);
		initialSnapshot.Definitions.Should().BeEmpty();
		initialSnapshot.Ownership.Should().BeEmpty();
		observedSnapshots.Should().BeGreaterThan(0);
		var finalSnapshot = sut.CaptureGlobalOptionConfiguration();
		finalSnapshot.Definitions.Should().HaveCount(optionCount);
		finalSnapshot.Ownership.Should().HaveCount(optionCount * 2);
	}

}
