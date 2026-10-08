using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
namespace WallpaperControl;
internal sealed record FeedEntry(string Id, string Title, string Description, string? Link, DateTimeOffset? Published, string? ImageUrl);
internal sealed record ParsedFeed(string Title, List<FeedEntry> Entries);
internal static partial class FeedParser
{
    internal static Uri? WebUri(string? value, Uri? basis = null)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        Uri? uri;
        if (basis != null) Uri.TryCreate(basis, value, out uri); else Uri.TryCreate(value, UriKind.Absolute, out uri);
        return uri is { IsAbsoluteUri: true } && uri.Scheme is "https" or "http" && uri.UserInfo.Length == 0 ? uri : null;
    }
    [GeneratedRegex("<[^>]*>", RegexOptions.Singleline)] private static partial Regex Tags();
    [GeneratedRegex("\\s+")] private static partial Regex Whitespace();
    [GeneratedRegex("<img[^>]+src\\s*=\\s*['\"]([^'\"]+)['\"]", RegexOptions.IgnoreCase)] private static partial Regex ImageTag();
    internal static string Plain(string text) => Whitespace().Replace(WebUtility.HtmlDecode(Tags().Replace(text, " ")), " ").Trim();
    internal static ParsedFeed Parse(Stream stream, Uri basis)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2_000_000 });
        var doc = XDocument.Load(reader);
        bool atom = doc.Root?.Name == XName.Get("feed", "http://www.w3.org/2005/Atom");
        bool rdf = doc.Root?.Name == XName.Get("RDF", "http://www.w3.org/1999/02/22-rdf-syntax-ns#");
        var channel = atom ? doc.Root : doc.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "channel");
        if (channel == null || (!atom && !rdf && doc.Root?.Name.LocalName != "rss")) throw new XmlException("Not an RSS or Atom feed");
        string Get(XElement parent, string key) => parent.Elements().FirstOrDefault(e => e.Name.LocalName == key)?.Value ?? "";
        var entries = new List<FeedEntry>();
        foreach (var item in (rdf ? doc.Root!.Elements() : channel.Elements()).Where(e => e.Name.LocalName == (atom ? "entry" : "item")).Take(100))
        {
            string html = Get(item, atom ? "summary" : "description");
            if (html.Length == 0) html = Get(item, atom ? "content" : "encoded");
            var linkElement = item.Elements().FirstOrDefault(e => e.Name.LocalName == "link" && (!atom || (string?)e.Attribute("rel") is null or "alternate"));
            Uri itemBase = basis;
            foreach (var ancestor in item.AncestorsAndSelf().Reverse())
                itemBase = WebUri((string?)ancestor.Attribute(XNamespace.Xml + "base"), itemBase) ?? itemBase;
            string? link = WebUri(atom ? (string?)linkElement?.Attribute("href") : linkElement?.Value, itemBase)?.AbsoluteUri;
            string date = Get(item, atom ? "published" : "pubDate"); if (date.Length == 0) date = Get(item, atom ? "updated" : "date");
            date = date.Replace(" GMT", " +0000", StringComparison.Ordinal);
            DateTimeOffset? published = DateTimeOffset.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed) ? parsed : null;
            string? image = item.Descendants().Where(e => e.Name.LocalName is "thumbnail" or "content" or "enclosure")
                .Where(e => e.Name.LocalName == "thumbnail" || ((string?)e.Attribute("type"))?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true || (string?)e.Attribute("medium") == "image")
                .Select(e => WebUri((string?)e.Attribute("url"), itemBase)?.AbsoluteUri).FirstOrDefault(s => s != null);
            if (image == null) { var match = ImageTag().Match(html); if (match.Success) image = WebUri(WebUtility.HtmlDecode(match.Groups[1].Value), itemBase)?.AbsoluteUri; }
            string title = Plain(Get(item, "title")); string description = Plain(html);
            if (title.Length == 0) title = description;
            string id = Get(item, atom ? "id" : "guid"); if (id.Length == 0) id = link ?? title + date;
            entries.Add(new(id, title[..Math.Min(title.Length, 512)], description[..Math.Min(description.Length, 2000)], link, published, image));
        }
        string feedTitle = Plain(Get(channel, "title"));
        return new(feedTitle[..Math.Min(feedTitle.Length, 512)], entries.DistinctBy(e => e.Id).OrderByDescending(e => e.Published).Take(60).ToList());
    }
}
