namespace WallpaperControl;

internal sealed record PriceWatchAlert(Guid Id, string Title, PriceWatchOffer Offer, int TargetCents);
internal sealed record PriceWatchMonitorState(DateTimeOffset? Attempt, DateTimeOffset? Success);

internal sealed partial class PriceWatchStore
{
    internal PriceWatchMonitorState ReadMonitorState(bool live)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_attempt,last_success FROM price_monitor_state WHERE live=$mode";
        command.Parameters.AddWithValue("$mode", live);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return new(null, null);
        return new(reader.IsDBNull(0) ? null : new DateTimeOffset(reader.GetInt64(0), TimeSpan.Zero),
            reader.IsDBNull(1) ? null : new DateTimeOffset(reader.GetInt64(1), TimeSpan.Zero));
    }

    internal void SaveMonitorState(bool live, DateTimeOffset now, bool success)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO price_monitor_state(live,last_attempt,last_success) VALUES($mode,$now,$success)
            ON CONFLICT(live) DO UPDATE SET last_attempt=$now,
                last_success=CASE WHEN $success IS NULL THEN last_success ELSE $success END
            """;
        command.Parameters.AddWithValue("$mode", live);
        command.Parameters.AddWithValue("$now", now.UtcTicks);
        command.Parameters.AddWithValue("$success", success ? now.UtcTicks : DBNull.Value);
        command.ExecuteNonQuery();
    }

    internal PriceWatchAlert? ObservePriceAlert(bool live, PriceWatchEntry expected, IReadOnlyList<int> shops,
        PriceWatchReply<PriceWatchPrices[]> reply, DateTimeOffset now)
    {
        if (expected.TargetCents == null) return null;
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using var current = connection.CreateCommand();
        current.Transaction = transaction;
        current.CommandText = """
            SELECT t.cents,COALESCE(a.reached,0),w.fetched_ticks FROM target_prices t
            JOIN watched w ON w.live=t.live AND w.id=t.id
            LEFT JOIN price_alerts a ON a.live=t.live AND a.id=t.id
            WHERE t.live=$mode AND t.id=$id
            """;
        BindKey(current, live, expected.Game.Id);
        bool wasReached;
        using (var reader = current.ExecuteReader())
        {
            if (!reader.Read() || reader.GetInt32(0) != expected.TargetCents
                || reader.IsDBNull(2) || reader.GetInt64(2) != reply.FetchedAt.UtcTicks) return null;
            wasReached = reader.GetInt32(1) != 0;
        }
        var entry = expected with { Snapshot = reply };
        var offers = PriceWatchShops.WidgetOffers(entry, shops, now);
        var cheapest = offers.FirstOrDefault(offer => PriceWatchTargetPrice.IsReached(entry, offer, now, shops));
        // Missing shops or expired offers cannot reliably establish an above-target transition.
        bool complete = shops.All(id => PriceWatchOfferDisplay.ForShop(entry, id)
            .Any(offer => PriceWatchShops.ValidOffer(offer, now)));
        if (cheapest == null && !complete) return null;
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = """
            INSERT INTO price_alerts(live,id,reached) VALUES($mode,$id,$reached)
            ON CONFLICT(live,id) DO UPDATE SET reached=$reached
            """;
        BindKey(update, live, expected.Game.Id);
        update.Parameters.AddWithValue("$reached", cheapest != null);
        update.ExecuteNonQuery();
        transaction.Commit();
        return cheapest != null && !wasReached ? new(expected.Game.Id, expected.Game.Title, cheapest, expected.TargetCents.Value) : null;
    }
}
