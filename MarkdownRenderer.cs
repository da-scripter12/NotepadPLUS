using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NotepadPLUS;

internal static class MarkdownRenderer
{
    private static readonly Regex FenceStart = new(@"^\s{0,3}(\x60{3,}|~{3,})(.*)$", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^ {0,3}(#{1,6})\s+(.+?)\s*#*\s*$", RegexOptions.Compiled);
    private static readonly Regex Quote = new(@"^ {0,3}> ?(.*)$", RegexOptions.Compiled);
    private static readonly Regex ListItem = new(@"^ {0,3}((?:\d+[.)])|[-*+])\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex HorizontalRule = new(@"^ {0,3}(?:(?:\*\s*){3,}|(?:-\s*){3,}|(?:_\s*){3,})$", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\((https?://[^\s)]+|mailto:[^\s)]+)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex InlineCode = new(@"\x60([^\x60]+)\x60", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*|__(.+?)__", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"(?<!\*)\*([^*\r\n]+)\*(?!\*)|(?<!_)_([^_\r\n]+)_(?!_)", RegexOptions.Compiled);
    private static readonly Regex Strike = new(@"~~(.+?)~~", RegexOptions.Compiled);

    public static string ToHtmlDocument(string markdown)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        html.Append("<meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">");
        html.Append("<style>");
        html.Append("body{font:15px/1.6 'Segoe UI',Arial,sans-serif;color:#252a30;margin:24px;max-width:900px;overflow-wrap:break-word}");
        html.Append("h1,h2{border-bottom:1px solid #e3e6ea;padding-bottom:5px}h1{font-size:1.8em}h2{font-size:1.45em}");
        html.Append("blockquote{border-left:3px solid #c7ced8;margin:12px 0;padding:2px 14px;color:#626b76;background:#f8f9fa}");
        html.Append("pre{background:#f3f5f7;border:1px solid #e2e5e9;border-radius:4px;padding:12px;white-space:pre-wrap}");
        html.Append("code{font-family:Consolas,'Courier New',monospace;background:#f3f5f7;padding:1px 4px;border-radius:3px}");
        html.Append("pre code{background:transparent;padding:0}a{color:#2369af}hr{border:0;border-top:1px solid #d7dce2;margin:20px 0}");
        html.Append("</style></head><body>");
        html.Append(RenderBlocks(markdown));
        html.Append("</body></html>");
        return html.ToString();
    }

    private static string RenderBlocks(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var output = new StringBuilder();
        var paragraph = new List<string>();
        var codeBlock = new StringBuilder();
        string? listTag = null;
        var inCodeBlock = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            output.Append("<p>");
            output.Append(RenderInline(string.Join("\n", paragraph)));
            output.Append("</p>");
            paragraph.Clear();
        }

        void CloseList()
        {
            if (listTag is null)
            {
                return;
            }

            output.Append("</").Append(listTag).Append('>');
            listTag = null;
        }

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];

            if (inCodeBlock)
            {
                var fence = Regex.Match(line, @"^\s{0,3}([\x60~]+)\s*$");
                if (fence.Success
                    && fence.Groups[1].Value[0] == fenceCharacter
                    && fence.Groups[1].Value.Length >= fenceLength)
                {
                    output.Append("<pre><code>");
                    output.Append(WebUtility.HtmlEncode(codeBlock.ToString()));
                    output.Append("</code></pre>");
                    codeBlock.Clear();
                    inCodeBlock = false;
                }
                else
                {
                    if (codeBlock.Length > 0)
                    {
                        codeBlock.Append('\n');
                    }

                    codeBlock.Append(line);
                }

                continue;
            }

            var fenceStart = FenceStart.Match(line);
            if (fenceStart.Success)
            {
                FlushParagraph();
                CloseList();
                fenceCharacter = fenceStart.Groups[1].Value[0];
                fenceLength = fenceStart.Groups[1].Value.Length;
                inCodeBlock = true;
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                CloseList();
                continue;
            }

            var heading = Heading.Match(line);
            if (heading.Success)
            {
                FlushParagraph();
                CloseList();
                var level = heading.Groups[1].Value.Length;
                output.Append("<h").Append(level).Append('>');
                output.Append(RenderInline(heading.Groups[2].Value));
                output.Append("</h").Append(level).Append('>');
                continue;
            }

            if (HorizontalRule.IsMatch(line))
            {
                FlushParagraph();
                CloseList();
                output.Append("<hr>");
                continue;
            }

            var quote = Quote.Match(line);
            if (quote.Success)
            {
                FlushParagraph();
                CloseList();
                var quoteLines = new List<string>();
                while (index < lines.Length)
                {
                    var quoteLine = Quote.Match(lines[index]);
                    if (!quoteLine.Success)
                    {
                        break;
                    }

                    quoteLines.Add(quoteLine.Groups[1].Value);
                    index++;
                }

                index--;
                output.Append("<blockquote><p>");
                output.Append(RenderInline(string.Join("\n", quoteLines)));
                output.Append("</p></blockquote>");
                continue;
            }

            var item = ListItem.Match(line);
            if (item.Success)
            {
                FlushParagraph();
                var marker = item.Groups[1].Value;
                var nextTag = char.IsDigit(marker[0]) ? "ol" : "ul";
                if (listTag != nextTag)
                {
                    CloseList();
                    listTag = nextTag;
                    output.Append('<').Append(listTag).Append('>');
                }

                output.Append("<li>").Append(RenderInline(item.Groups[2].Value)).Append("</li>");
                continue;
            }

            CloseList();
            paragraph.Add(line);
        }

        if (inCodeBlock)
        {
            output.Append("<pre><code>");
            output.Append(WebUtility.HtmlEncode(codeBlock.ToString()));
            output.Append("</code></pre>");
        }

        FlushParagraph();
        CloseList();
        return output.ToString();
    }

    private static string RenderInline(string source)
    {
        var tokens = new List<string>();
        var prefix = "MDTOKEN" + Guid.NewGuid().ToString("N") + "VALUE";

        string Protect(string html)
        {
            var token = prefix + tokens.Count.ToString("X") + "END";
            tokens.Add(html);
            return token;
        }

        var text = WebUtility.HtmlEncode(source);
        text = InlineCode.Replace(text, match => Protect("<code>" + match.Groups[1].Value + "</code>"));
        text = Link.Replace(text, match =>
        {
            var label = match.Groups[1].Value;
            var address = WebUtility.HtmlEncode(WebUtility.HtmlDecode(match.Groups[2].Value));
            return Protect("<a href=\"" + address + "\">" + label + "</a>");
        });
        text = Bold.Replace(text, match =>
        {
            var content = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return "<strong>" + content + "</strong>";
        });
        text = Strike.Replace(text, match => "<del>" + match.Groups[1].Value + "</del>");
        text = Italic.Replace(text, match =>
        {
            var content = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            return "<em>" + content + "</em>";
        });

        for (var index = 0; index < tokens.Count; index++)
        {
            text = text.Replace(prefix + index.ToString("X") + "END", tokens[index], StringComparison.Ordinal);
        }

        return text.Replace("\n", "<br>\n", StringComparison.Ordinal);
    }
}
