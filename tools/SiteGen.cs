// SiteGen: builds the website in docs/ (published with GitHub Pages) from the bilingual template site/index.html.
//
//   dotnet run tools/SiteGen.cs
//
// Each language gets its own page, so search engines index both: docs/index.html (Spanish) and docs/en/index.html
// (English). In the template, elements with class="es" or class="en" are kept only on their page, [[español||English]]
// picks one side, and {{site}}, {{download}} and {{version}} are filled in. The English page lives one folder down, so
// its relative links get a "../", and it shows the English screenshots from docs/img/en/ wherever there is one.
// The FAQ is also written as JSON-LD for search engines.

using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

const string Site = "https://aurgo.github.io/SpaceAnalyzer/";
const string Download = "https://github.com/aurgo/SpaceAnalyzer/releases/latest/download/";

var root = FindRoot();
var docs = Path.Combine(root, "docs");
var template = File.ReadAllText(Path.Combine(root, "site", "index.html")).ReplaceLineEndings("\n");
var version = Regex.Match(File.ReadAllText(Path.Combine(root, "Directory.Build.props")), "<Version>(.*?)</Version>").Groups[1].Value;

foreach (var lang in new[] { "es", "en" })
{
    var html = Build(template, lang);
    var path = lang == "es" ? Path.Combine(docs, "index.html") : Path.Combine(docs, "en", "index.html");
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, html, new UTF8Encoding(false));
    Console.WriteLine($"{Path.GetRelativePath(root, path)}: {html.Length / 1024} KB");
}
return 0;

string Build(string html, string lang)
{
    bool english = lang == "en";
    html = Regex.Replace(html, @"\[\[(.*?)\|\|(.*?)\]\]", m =>
    {
        if (m.Value.IndexOf("[[", 2) >= 0) throw new FormatException($"Unbalanced [[ ]] near: {m.Value[..Math.Min(80, m.Value.Length)]}");
        return m.Groups[english ? 2 : 1].Value;
    }, RegexOptions.Singleline);
    html = html.Replace("{{site}}", Site).Replace("{{download}}", Download).Replace("{{version}}", version);
    html = KeepLanguage(html, lang);
    if (english) html = MoveDown(html);
    html = html.Replace("<!--faq-->", FaqJsonLd(html));

    foreach (var leftover in new[] { "[[", "{{", "class=\"es\"", "class=\"en\"", "<!--faq-->" })
        if (html.Contains(leftover)) throw new FormatException($"{lang}: '{leftover}' is left in the page");

    const string Doctype = "<!doctype html>\n";
    if (!html.StartsWith(Doctype)) throw new FormatException("The template must start with <!doctype html>");
    return Doctype + "<!-- Generated from site/index.html by tools/SiteGen.cs. Edit the template, then run: dotnet run tools/SiteGen.cs -->\n"
        + html[Doctype.Length..];
}

// Removes the elements of the other language and the class="es|en" markers of this one.
static string KeepLanguage(string html, string lang)
{
    var open = new Regex(@"<([a-z][a-z0-9]*)\b[^>]*?( class=""(es|en)"")[^>]*>");
    var sb = new StringBuilder();
    int pos = 0;
    for (var m = open.Match(html, pos); m.Success; m = open.Match(html, pos))
    {
        var tag = m.Groups[1].Value;
        if (m.Groups[3].Value == lang)
        {
            sb.Append(html, pos, m.Groups[2].Index - pos);
            pos = m.Groups[2].Index + m.Groups[2].Length;
            continue;
        }

        int end = ClosingTagEnd(html, tag, m.Index + m.Length), start = m.Index;
        int lineStart = start, lineEnd = end;
        while (lineStart > pos && html[lineStart - 1] is ' ' or '\t') lineStart--;
        while (lineEnd < html.Length && html[lineEnd] is ' ' or '\t') lineEnd++;
        if ((lineStart == 0 || html[lineStart - 1] == '\n') && (lineEnd == html.Length || html[lineEnd] == '\n'))
            (start, end) = (lineStart, Math.Min(lineEnd + 1, html.Length)); // the element had the whole line to itself
        sb.Append(html, pos, start - pos);
        pos = end;
    }
    return sb.Append(html, pos, html.Length - pos).ToString();
}

static int ClosingTagEnd(string html, string tag, int from)
{
    var tags = new Regex($@"<(/?){tag}\b[^>]*>");
    int depth = 1;
    for (var m = tags.Match(html, from); m.Success; m = m.NextMatch())
        if ((depth += m.Groups[1].Value == "/" ? -1 : 1) == 0) return m.Index + m.Length;
    throw new FormatException($"<{tag}> at {from} is never closed");
}

// The English page is in docs/en/: relative links need a "../", and screenshots come from img/en/ when there is one.
string MoveDown(string html) => Regex.Replace(html, @"\b(src|href|data-full|srcset)=""([^""]*)""", m =>
{
    var urls = m.Groups[1].Value == "srcset" ? m.Groups[2].Value.Split(", ") : [m.Groups[2].Value];
    for (int i = 0; i < urls.Length; i++)
    {
        var parts = urls[i].Split(' ', 2); // srcset entries end with a width, like "img/hero.webp 1400w"
        var url = parts[0];
        if (url.Length == 0 || url[0] is '#' or '/' || url.StartsWith("../") || url.Contains(':')) continue;
        if (url.StartsWith("img/") && File.Exists(Path.Combine(docs, "img", "en", url[4..]))) url = "img/en/" + url[4..];
        parts[0] = "../" + url;
        urls[i] = string.Join(' ', parts);
    }
    return $"{m.Groups[1].Value}=\"{string.Join(", ", urls)}\"";
});

// schema.org FAQPage built from the page's own <details><summary>question</summary><p>answer</p></details> list.
static string FaqJsonLd(string html)
{
    var items = Regex.Matches(html, @"<details[^>]*>\s*<summary>(.*?)</summary>\s*<p>(.*?)</p>\s*</details>", RegexOptions.Singleline);
    if (items.Count == 0) throw new FormatException("No FAQ found");

    var buffer = new MemoryStream();
    using (var w = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n", Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
    {
        w.WriteStartObject();
        w.WriteString("@context", "https://schema.org");
        w.WriteString("@type", "FAQPage");
        w.WriteStartArray("mainEntity");
        foreach (Match item in items)
        {
            w.WriteStartObject();
            w.WriteString("@type", "Question");
            w.WriteString("name", PlainText(item.Groups[1].Value));
            w.WriteStartObject("acceptedAnswer");
            w.WriteString("@type", "Answer");
            w.WriteString("text", PlainText(item.Groups[2].Value));
            w.WriteEndObject();
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteEndObject();
    }
    var json = Encoding.UTF8.GetString(buffer.ToArray());
    if (json.Contains('<')) throw new FormatException("The FAQ text can't contain '<' inside a <script>");
    return "<script type=\"application/ld+json\">\n" + json + "\n</script>";
}

static string PlainText(string html) =>
    Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]+>", "")), @"\s+", " ").Trim();

static string FindRoot()
{
    for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "SpaceAnalyzer.slnx"))) return dir.FullName;
    throw new DirectoryNotFoundException("Run this from inside the SpaceAnalyzer repository.");
}
