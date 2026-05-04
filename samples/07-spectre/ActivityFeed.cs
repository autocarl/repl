using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Repl;

internal sealed record ActivityEvent(
	[property: Display(Name = "#", Order = 0)] int Id,
	[property: Display(Name = "At", Order = 1)] string At,
	[property: Display(Name = "Team", Order = 2)] string Team,
	[property: Display(Name = "Status", Order = 3)] string Status,
	[property: Display(Name = "Work Item", Order = 4)] string WorkItem);

internal sealed class ActivityFeed
{
	private readonly List<ActivityEvent> _items = CreateItems();

	public ReplPage<ActivityEvent> Query(IReplPagingContext paging)
	{
		ArgumentNullException.ThrowIfNull(paging);

		var offset = paging.AllRequested ? 0 : ParseOffset(paging.Cursor);
		var items = paging.AllRequested
			? _items
			: _items.Skip(offset).Take(paging.SuggestedPageSize).ToList();

		var nextOffset = offset + items.Count;
		var nextCursor = !paging.AllRequested && nextOffset < _items.Count
			? nextOffset.ToString(CultureInfo.InvariantCulture)
			: null;

		return paging.Page(items, nextCursor, _items.Count);
	}

	private static int ParseOffset(string? cursor) =>
		int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset > 0
			? offset
			: 0;

	private static List<ActivityEvent> CreateItems()
	{
		string[] teams = ["platform", "growth", "support", "data", "security"];
		string[] statuses = ["triaged", "running", "blocked", "reviewed", "done"];
		var start = new DateTimeOffset(2026, 2, 9, 9, 30, 0, TimeSpan.Zero);

		return Enumerable.Range(1, 320)
			.Select(i =>
			{
				var team = teams[(i - 1) % teams.Length];
				var status = statuses[(i - 1) % statuses.Length];

				return new ActivityEvent(
					i,
					start.AddMinutes(i * 11).ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
					team,
					status,
					$"{team}-{i:0000} {status}");
			})
			.ToList();
	}
}
