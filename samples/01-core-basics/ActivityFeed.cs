using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Repl;

sealed record ActivityEvent(
	[property: Display(Name = "#", Order = 0)] int Id,
	[property: Display(Name = "At", Order = 1)] string At,
	[property: Display(Name = "Area", Order = 2)] string Area,
	[property: Display(Name = "Event", Order = 3)] string Event,
	[property: Display(Name = "Summary", Order = 4)] string Summary);

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
		string[] areas = ["identity", "billing", "catalog", "search", "import", "reporting"];
		string[] events = ["validated", "queued", "indexed", "exported", "reconciled", "notified"];
		var start = new DateTimeOffset(2026, 1, 12, 8, 0, 0, TimeSpan.Zero);

		return Enumerable.Range(1, 250)
			.Select(i =>
			{
				var area = areas[(i - 1) % areas.Length];
				var eventName = events[(i - 1) % events.Length];

				return new ActivityEvent(
					i,
					start.AddMinutes(i * 7).ToString("yyyy-MM-dd HH:mm'Z'", CultureInfo.InvariantCulture),
					area,
					eventName,
					$"{area} batch {((i - 1) / 5) + 1} {eventName} successfully");
			})
			.ToList();
	}
}
