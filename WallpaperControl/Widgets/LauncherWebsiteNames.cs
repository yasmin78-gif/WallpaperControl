using System.Net;
using System.Text.RegularExpressions;

namespace WallpaperControl;

internal static class LauncherWebsiteNames
{
    internal static string Host(Uri url)=>url.Host.StartsWith("www.",StringComparison.OrdinalIgnoreCase)?url.Host[4..]:url.Host;
    internal static string FromHtml(string html,Uri url)
    {
        try
        {
            // Ignore comment/script text; no HTML is executed or rendered.
            html=Regex.Replace(html,@"<!--[\s\S]*?-->|<(script|style)\b[^>]*>[\s\S]*?</\1\s*>","",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(150));
            string? site=null,application=null;
            foreach(Match meta in Regex.Matches(html,@"<meta\b[^>]{0,8192}>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(150)).Cast<Match>().Take(256))
            {
                var attributes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
                foreach(Match attribute in Regex.Matches(meta.Value,"([\\w:-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",RegexOptions.None,TimeSpan.FromMilliseconds(150)))
                    attributes[attribute.Groups[1].Value]=attribute.Groups[2].Success?attribute.Groups[2].Value:attribute.Groups[3].Success?attribute.Groups[3].Value:attribute.Groups[4].Value;
                string? value=Clean(attributes.GetValueOrDefault("content"));
                if(attributes.GetValueOrDefault("property",attributes.GetValueOrDefault("name","")).Equals("og:site_name",StringComparison.OrdinalIgnoreCase))site??=value;
                if(attributes.GetValueOrDefault("name","").Equals("application-name",StringComparison.OrdinalIgnoreCase))application??=value;
            }
            if(site!=null)return site;
            if(application!=null)return application;
            var title=Regex.Match(html,@"<title\b[^>]{0,8192}>([\s\S]{0,8192}?)</title\s*>",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(150));
            string? name=title.Success?Clean(title.Groups[1].Value):null;
            if(name!=null)
            {
                // Strip only explicit generic homepage/official-site labels, not arbitrary taglines or sections.
                name=Regex.Replace(name,@"\s+[|–—-]\s+(?:Home|Homepage|Startseite|Official (?:Website|Site))$","",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
                name=Regex.Replace(name,@"^(?:Home|Homepage|Startseite)\s+[|–—-]\s+","",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100));
                return Clean(name)??Host(url);
            }
        }
        catch(RegexMatchTimeoutException) { }
        return Host(url);
    }
    private static string? Clean(string? value)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>8192)return null;
        value=WebUtility.HtmlDecode(value);
        value=Regex.Replace(value,@"\s+"," ",RegexOptions.None,TimeSpan.FromMilliseconds(100)).Trim();
        if(value.Length==0||value.Length>160||value.Any(char.IsControl)||value.Contains('<')||value.Contains('>'))return null;
        if(LauncherTargets.IsWebsite(value)||Regex.IsMatch(value,@"^(?:Home|Homepage|Startseite|Untitled|Official (?:Website|Site)|Just a moment\.?\.?.?|Access denied|Forbidden|Not Found|Error|(?:403|404|500)(?:\s+.*)?)$",RegexOptions.IgnoreCase,TimeSpan.FromMilliseconds(100)))return null;
        return value;
    }
}
