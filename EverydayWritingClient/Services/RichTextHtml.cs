using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using HtmlAgilityPack;

namespace EverydayWritingClient.Services;

public static class RichTextHtml
{
    public static void Load(FlowDocument document, string? html)
    {
        document.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(html))
        {
            document.Blocks.Add(new Paragraph());
            return;
        }

        if (!html.Contains('<'))
        {
            document.Blocks.Add(ParagraphFromText(html, null));
            return;
        }

        var parsed = new HtmlDocument();
        parsed.LoadHtml(html);
        var root = parsed.DocumentNode;
        if (root.SelectSingleNode("//body") is { } body)
        {
            root = body;
        }

        foreach (var node in root.ChildNodes)
        {
            AppendBlock(document.Blocks, node);
        }

        if (document.Blocks.Count == 0)
        {
            document.Blocks.Add(new Paragraph());
        }
    }

    public static string Save(FlowDocument document)
    {
        var builder = new StringBuilder();
        foreach (var block in document.Blocks)
        {
            WriteBlock(builder, block);
        }

        return builder.Length == 0 ? "<p></p>" : builder.ToString();
    }

    public static string PlainText(FlowDocument document)
    {
        var range = new TextRange(document.ContentStart, document.ContentEnd);
        return range.Text?.Trim() ?? "";
    }

    private static void AppendBlock(BlockCollection blocks, HtmlNode node)
    {
        switch (node.Name.ToLowerInvariant())
        {
            case "#text":
                if (!string.IsNullOrWhiteSpace(node.InnerText))
                {
                    blocks.Add(ParagraphFromText(WebUtility.HtmlDecode(node.InnerText), null));
                }
                break;
            case "h1":
                blocks.Add(MakeParagraph(node, "h1", 28, FontWeights.SemiBold));
                break;
            case "h2":
                blocks.Add(MakeParagraph(node, "h2", 22, FontWeights.SemiBold));
                break;
            case "h3":
                blocks.Add(MakeParagraph(node, "h3", 18, FontWeights.SemiBold));
                break;
            case "blockquote":
                var quote = MakeParagraph(node, "quote", 16, FontWeights.Normal);
                quote.Margin = new Thickness(12, 4, 0, 4);
                quote.Foreground = new SolidColorBrush(Color.FromRgb(82, 82, 82));
                blocks.Add(quote);
                break;
            case "ul":
            case "ol":
                var list = new FlowList
                {
                    MarkerStyle = node.Name.Equals("ol", StringComparison.OrdinalIgnoreCase)
                        ? TextMarkerStyle.Decimal
                        : TextMarkerStyle.Disc,
                    Margin = new Thickness(18, 4, 0, 4),
                };
                foreach (var child in node.ChildNodes.Where(item => item.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
                {
                    var item = new ListItem();
                    var innerBlocks = child.ChildNodes.Where(n => n.NodeType == HtmlNodeType.Element).ToList();
                    if (innerBlocks.Count == 0)
                    {
                        item.Blocks.Add(MakeParagraph(child, null, 16, FontWeights.Normal));
                    }
                    else
                    {
                        foreach (var inner in innerBlocks)
                        {
                            AppendBlock(item.Blocks, inner);
                        }
                    }

                    if (item.Blocks.Count == 0)
                    {
                        item.Blocks.Add(new Paragraph());
                    }

                    list.ListItems.Add(item);
                }
                blocks.Add(list);
                break;
            case "p":
            case "div":
                blocks.Add(MakeParagraph(node, null, 16, FontWeights.Normal));
                break;
            case "br":
                blocks.Add(new Paragraph());
                break;
            case "pre":
                var codeBlock = new Paragraph
                {
                    Tag = "pre",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 15,
                    Margin = new Thickness(0, 0, 0, 8),
                };
                AppendInlines(codeBlock.Inlines, node);
                blocks.Add(codeBlock);
                break;
            case "hr":
                blocks.Add(new Paragraph
                {
                    Tag = "hr",
                    Margin = new Thickness(0, 8, 0, 8),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(229, 229, 229)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                });
                break;
            default:
                if (node.NodeType != HtmlNodeType.Element)
                {
                    break;
                }

                if (node.Name is "span" or "a" or "strong" or "em" or "b" or "i" or "u" or "s" or "code")
                {
                    blocks.Add(MakeParagraph(node, null, 16, FontWeights.Normal));
                    break;
                }

                blocks.Add(Preserve(node));
                break;
        }
    }

    private static Paragraph MakeParagraph(HtmlNode node, string? tag, double size, FontWeight weight)
    {
        var paragraph = new Paragraph
        {
            Tag = tag,
            FontSize = size,
            FontWeight = weight,
            Margin = new Thickness(0, 0, 0, 8),
            LineHeight = size + 10,
        };
        AppendInlines(paragraph.Inlines, node);
        return paragraph;
    }

    private static Paragraph ParagraphFromText(string text, string? tag)
    {
        var paragraph = new Paragraph { Tag = tag, Margin = new Thickness(0, 0, 0, 8) };
        paragraph.Inlines.Add(new Run(text));
        return paragraph;
    }

    private static void AppendInlines(InlineCollection inlines, HtmlNode node)
    {
        foreach (var child in node.ChildNodes)
        {
            switch (child.Name.ToLowerInvariant())
            {
                case "#text":
                    var text = WebUtility.HtmlDecode(child.InnerText);
                    if (text.Length > 0)
                    {
                        inlines.Add(new Run(text));
                    }
                    break;
                case "br":
                    inlines.Add(new LineBreak());
                    break;
                case "strong":
                case "b":
                    inlines.Add(Wrap(child, weight: FontWeights.Bold));
                    break;
                case "em":
                case "i":
                    inlines.Add(Wrap(child, style: FontStyles.Italic));
                    break;
                case "u":
                    inlines.Add(Wrap(child, underline: true));
                    break;
                case "code":
                    var code = Wrap(child);
                    code.FontFamily = new FontFamily("Consolas");
                    code.Tag = "code";
                    inlines.Add(code);
                    break;
                case "s":
                case "strike":
                case "del":
                    var strike = Wrap(child);
                    strike.TextDecorations = TextDecorations.Strikethrough;
                    inlines.Add(strike);
                    break;
                case "a":
                    var href = child.GetAttributeValue("href", "");
                    var link = new Hyperlink { Tag = href };
                    if (Uri.TryCreate(href, UriKind.RelativeOrAbsolute, out var uri))
                    {
                        link.NavigateUri = uri;
                    }

                    AppendInlines(link.Inlines, child);
                    inlines.Add(link);
                    break;
                case "p":
                case "span":
                case "div":
                    AppendInlines(inlines, child);
                    break;
                default:
                    if (child.NodeType == HtmlNodeType.Text)
                    {
                        inlines.Add(new Run(WebUtility.HtmlDecode(child.InnerText)));
                    }
                    else
                    {
                        AppendInlines(inlines, child);
                    }
                    break;
            }
        }
    }

    private static Span Wrap(HtmlNode node, FontWeight? weight = null, FontStyle? style = null, bool underline = false)
    {
        var span = new Span();
        if (weight.HasValue)
        {
            span.FontWeight = weight.Value;
        }

        if (style.HasValue)
        {
            span.FontStyle = style.Value;
        }

        if (underline)
        {
            span.TextDecorations = TextDecorations.Underline;
        }

        AppendInlines(span.Inlines, node);
        return span;
    }

    private static void WriteBlock(StringBuilder builder, Block block)
    {
        switch (block)
        {
            case Paragraph paragraph:
                if (paragraph.Tag is RawHtml raw && Compact(BlockText(paragraph)) == Compact(raw.Plain))
                {
                    builder.Append(raw.Html);
                    return;
                }

                var tag = paragraph.Tag as string;
                if (tag == "hr")
                {
                    builder.Append("<hr />");
                    return;
                }

                if (tag == "pre")
                {
                    builder.Append("<pre>");
                    WriteInlines(builder, paragraph.Inlines);
                    builder.Append("</pre>");
                    return;
                }

                var name = tag is "h1" or "h2" or "h3" ? tag : "p";
                if (tag == "quote")
                {
                    builder.Append("<blockquote><p>");
                    WriteInlines(builder, paragraph.Inlines);
                    builder.Append("</p></blockquote>");
                    return;
                }

                builder.Append('<').Append(name).Append('>');
                WriteInlines(builder, paragraph.Inlines);
                builder.Append("</").Append(name).Append('>');
                break;
            case FlowList list:
                var listTag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
                builder.Append('<').Append(listTag).Append('>');
                foreach (var item in list.ListItems)
                {
                    builder.Append("<li>");
                    foreach (var child in item.Blocks)
                    {
                        WriteBlock(builder, child);
                    }
                    builder.Append("</li>");
                }
                builder.Append("</").Append(listTag).Append('>');
                break;
            case Section section:
                foreach (var child in section.Blocks)
                {
                    WriteBlock(builder, child);
                }
                break;
        }
    }

    private static void WriteInlines(StringBuilder builder, InlineCollection inlines)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Hyperlink link:
                    var href = link.Tag as string ?? link.NavigateUri?.ToString() ?? "";
                    builder.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">");
                    WriteInlines(builder, link.Inlines);
                    builder.Append("</a>");
                    break;
                case Run run:
                    WriteRun(builder, run.Text, run.FontWeight, run.FontStyle, run.TextDecorations);
                    break;
                case Span span:
                    var open = new StringBuilder();
                    var close = new StringBuilder();
                    if (span.FontWeight == FontWeights.Bold || span.FontWeight == FontWeights.SemiBold)
                    {
                        open.Append("<strong>");
                        close.Insert(0, "</strong>");
                    }

                    if (span.FontStyle == FontStyles.Italic)
                    {
                        open.Append("<em>");
                        close.Insert(0, "</em>");
                    }

                    if (span.TextDecorations != null && span.TextDecorations.Contains(TextDecorations.Underline[0]))
                    {
                        open.Append("<u>");
                        close.Insert(0, "</u>");
                    }

                    if (span.TextDecorations != null && span.TextDecorations.Contains(TextDecorations.Strikethrough[0]))
                    {
                        open.Append("<s>");
                        close.Insert(0, "</s>");
                    }

                    if (span.Tag as string == "code")
                    {
                        open.Append("<code>");
                        close.Insert(0, "</code>");
                    }

                    builder.Append(open);
                    WriteInlines(builder, span.Inlines);
                    builder.Append(close);
                    break;
                case LineBreak:
                    builder.Append("<br />");
                    break;
            }
        }
    }

    private static void WriteRun(StringBuilder builder, string? text, FontWeight weight, FontStyle style, TextDecorationCollection? decorations)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var encoded = WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br />");
        if (weight == FontWeights.Bold || weight == FontWeights.SemiBold)
        {
            encoded = $"<strong>{encoded}</strong>";
        }

        if (style == FontStyles.Italic)
        {
            encoded = $"<em>{encoded}</em>";
        }

        if (decorations != null && decorations.Contains(TextDecorations.Underline[0]))
        {
            encoded = $"<u>{encoded}</u>";
        }

        if (decorations != null && decorations.Contains(TextDecorations.Strikethrough[0]))
        {
            encoded = $"<s>{encoded}</s>";
        }

        builder.Append(encoded);
    }

    private static Paragraph Preserve(HtmlNode node)
    {
        var plain = WebUtility.HtmlDecode(node.InnerText);
        var paragraph = new Paragraph
        {
            Tag = new RawHtml(node.OuterHtml, plain),
            Margin = new Thickness(0, 0, 0, 8),
        };
        paragraph.Inlines.Add(new Run(string.IsNullOrWhiteSpace(plain) ? "（这里有一段网页端的复杂内容）" : plain.Trim()));
        return paragraph;
    }

    private static string BlockText(Paragraph paragraph)
    {
        return new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text ?? "";
    }

    private static string Compact(string text)
    {
        return string.Join(" ", text.Replace('\r', ' ').Replace('\n', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class RawHtml(string html, string plain)
    {
        public string Html { get; } = html;
        public string Plain { get; } = plain;
    }
}
