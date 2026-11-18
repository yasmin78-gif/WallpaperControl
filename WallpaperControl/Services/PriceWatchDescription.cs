namespace WallpaperControl;

internal sealed record PriceWatchDescription(Guid ItadId, string Text, string Language, string Source,
    bool IsBaseGameFallback = false, Guid? BaseGameId = null, DateTimeOffset? ExpiresAt = null)
{
    internal static PriceWatchDescription? Select(PriceWatchGame game, IEnumerable<PriceWatchDescription> descriptions,
        DateTimeOffset now)
    {
        return descriptions
            .Where(item => item.ItadId == game.Id && !string.IsNullOrWhiteSpace(item.Text)
                && !string.IsNullOrWhiteSpace(item.Source) && (item.ExpiresAt == null || item.ExpiresAt > now)
                && (!item.IsBaseGameFallback || (game.Type == "package" && item.BaseGameId is Guid parent
                    && parent != Guid.Empty && parent != game.Id)))
            .Select(item => (Item: item, Language: item.Language.Split('-', 2)[0]))
            .Where(candidate => candidate.Language.Equals("de", StringComparison.OrdinalIgnoreCase)
                || candidate.Language.Equals("en", StringComparison.OrdinalIgnoreCase))
            .OrderBy(candidate => candidate.Item.IsBaseGameFallback)
            .ThenBy(candidate => !candidate.Language.Equals("de", StringComparison.OrdinalIgnoreCase))
            .Select(candidate => candidate.Item)
            .FirstOrDefault();
    }
}
