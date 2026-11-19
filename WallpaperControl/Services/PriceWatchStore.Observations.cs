using Microsoft.Data.Sqlite;

namespace WallpaperControl;

internal sealed record PriceWatchObservation(Guid Id, bool Live, int Shop, DateTimeOffset CapturedAt,
    int Cents, int? RegularCents, string Currency = "EUR", DateTimeOffset? Expiry = null, string Source = "local");
internal sealed record PriceWatchLocalPoint(PriceWatchObservation Observation, bool IsLast);

internal sealed partial class PriceWatchStore
{
    private static void SaveObservations(SqliteConnection connection, SqliteTransaction transaction, bool live,
        Guid id, PriceWatchReply<PriceWatchPrices[]> reply, DateTimeOffset now)
    {
        // fetchedAt is the proxy retrieval time, including cache hits; never substitute local 'now'.
        if (reply.FetchedAt == default || reply.FetchedAt > now || reply.FetchedAt < now.AddDays(-90)
            || reply.Data.Length != 1 || reply.Data[0].Id != id) return;
        foreach (var group in reply.Data[0].Deals.Where(o => o != null && PriceWatchShops.ValidOffer(o, now)).GroupBy(o => o.Shop.Id))
        {
            var offer = group.MinBy(o => o.Price.AmountInt)!;
            using var command = connection.CreateCommand(); command.Transaction = transaction;
            command.CommandText = """
                INSERT OR IGNORE INTO price_observations
                    (live,id,shop,captured_ticks,cents,regular_cents,currency,source,expiry_ticks)
                VALUES($mode,$id,$shop,$time,$price,$regular,'EUR','local',$expiry)
                """;
            BindKey(command, live, id);
            command.Parameters.AddWithValue("$shop", offer.Shop.Id);
            command.Parameters.AddWithValue("$time", reply.FetchedAt.UtcTicks);
            command.Parameters.AddWithValue("$price", offer.Price.AmountInt);
            command.Parameters.AddWithValue("$regular", offer.Regular.AmountInt);
            command.Parameters.AddWithValue("$expiry", (object?)offer.Expiry?.UtcTicks ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
    }

    internal PriceWatchObservation[] ReadObservations(bool live, Guid id)
    {
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT shop,captured_ticks,cents,regular_cents,currency,expiry_ticks,source
            FROM price_observations WHERE live=$mode AND id=$id ORDER BY captured_ticks,shop
            """;
        BindKey(command, live, id); using var reader = command.ExecuteReader();
        var result = new List<PriceWatchObservation>();
        while (reader.Read()) result.Add(new(id, live, reader.GetInt32(0), new(reader.GetInt64(1), TimeSpan.Zero),
            reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetInt32(3), reader.GetString(4),
            reader.IsDBNull(5) ? null : new DateTimeOffset(reader.GetInt64(5), TimeSpan.Zero), reader.GetString(6)));
        return result.ToArray();
    }
}

internal static class PriceWatchLocalPrices
{
    internal static PriceWatchLocalPoint[] Create(PriceWatchEntry entry, bool live, IEnumerable<PriceWatchObservation> observations,
        int[] shops, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now)
    {
        bool InRange(PriceWatchObservation p) => p.Id == entry.Game.Id && p.Live == live && shops.Contains(p.Shop)
            && p.Source == "local" && p.Currency == "EUR" && p.Cents >= 0 && p.RegularCents is not < 0
            && p.CapturedAt >= from && p.CapturedAt <= to && p.CapturedAt <= now
            && (p.Expiry == null || p.Expiry > p.CapturedAt);
        // A historical observation records validity at capture; expiry today only disqualifies a latest-offer diamond.
        var points = observations.Where(InRange).GroupBy(p => (p.Shop, p.CapturedAt))
            .ToDictionary(g => g.Key, g => new PriceWatchLocalPoint(g.First(), false));
        if (entry.Snapshot is { } snapshot && snapshot.FetchedAt != default)
        {
            foreach (int shop in shops)
            {
                var offer = PriceWatchOfferDisplay.ForShop(entry, shop).Where(o => PriceWatchShops.ValidOffer(o, now))
                    .MinBy(o => o.Price.AmountInt);
                if (offer == null) continue;
                var observation = new PriceWatchObservation(entry.Game.Id, live, shop, snapshot.FetchedAt,
                    offer.Price.AmountInt, offer.Regular.AmountInt, "EUR", offer.Expiry);
                if (InRange(observation)) points[(shop, snapshot.FetchedAt)] = new(observation, true);
            }
        }
        return points.Values.OrderBy(p => p.Observation.CapturedAt).ThenBy(p => p.Observation.Shop).ToArray();
    }
}
