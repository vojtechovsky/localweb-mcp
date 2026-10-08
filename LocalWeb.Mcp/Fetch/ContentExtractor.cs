using System.Text.RegularExpressions;
using LocalWeb.Mcp.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReverseMarkdown;
using SmartReader;

namespace LocalWeb.Mcp.Fetch;

/// <summary>
/// Turns HTML into readable Markdown. Uses SmartReader for main-content
/// extraction and ReverseMarkdown for the HTML to Markdown conversion, with a
/// graceful fallback to the raw HTML when extraction fails.
/// </summary>
public sealed partial class ContentExtractor
{
    private readonly LocalWebOptions _options;
    private readonly ILogger<ContentExtractor> _logger;
    private readonly Converter _converter;

    public ContentExtractor(IOptions<LocalWebOptions> options, ILogger<ContentExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;

        var config = new Config
        {
            GithubFlavored = true,
        };
        config.Tags.Unknown = Config.UnknownTagsOption.Bypass;
        config.Formatting.RemoveComments = true;
        _converter = new Converter(config);
    }

    public ExtractedContent Extract(string url, string html)
    {
        var title = string.Empty;
        var contentHtml = html;

        try
        {
            var reader = new Reader(url, html);
            var article = reader.GetArticle();
            if (article is { IsReadable: true } && !string.IsNullOrWhiteSpace(article.Content))
            {
                title = article.Title ?? string.Empty;
                contentHtml = article.Content!;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "SmartReader extraction failed for {Url}; using raw HTML.", url);
        }

        var markdown = ToMarkdown(contentHtml);
        return new ExtractedContent(title, markdown, markdown.Length);
    }

    public ExtractedContent ExtractPlainText(string title, string text)
    {
        var normalized = NormalizeLineBreaks(text);
        return new ExtractedContent(title, normalized, normalized.Length);
    }

    /// <summary>
    /// Decides whether the browser fallback should be used: either too little
    /// text was extracted, or the page explicitly asks for JavaScript.
    /// </summary>
    public bool NeedsBrowser(string html, ExtractedContent extracted)
    {
        if (extracted.TextLength < _options.MinExtractCharsForBrowser)
        {
            return true;
        }

        return JavaScriptRequiredRegex().IsMatch(html);
    }

    private string ToMarkdown(string html)
    {
        string markdown;
        try
        {
            markdown = _converter.Convert(html);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "HTML to Markdown conversion failed; stripping tags instead.");
            markdown = StripTags(html);
        }

        return NormalizeLineBreaks(markdown);
    }

    private static string NormalizeLineBreaks(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
        // Collapse runs of three or more newlines down to two.
        normalized = MultipleNewLinesRegex().Replace(normalized, "\n\n");
        return normalized.Trim();
    }

    private static string StripTags(string html)
    {
        var withoutScripts = ScriptAndStyleRegex().Replace(html, " ");
        var withoutTags = TagRegex().Replace(withoutScripts, " ");
        return System.Net.WebUtility.HtmlDecode(withoutTags);
    }

    [GeneratedRegex("<(script|style)\\b[^>]*>.*?</\\1>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptAndStyleRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("\n{3,}")]
    private static partial Regex MultipleNewLinesRegex();

    [GeneratedRegex(
        "enable\\s+javascript|javascript\\s+is\\s+required|javascript\\s+is\\s+disabled|please\\s+enable\\s+(js|javascript)|you\\s+need\\s+to\\s+enable\\s+javascript",
        RegexOptions.IgnoreCase)]
    private static partial Regex JavaScriptRequiredRegex();
}
