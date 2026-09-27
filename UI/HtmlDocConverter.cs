using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace GameNotes.UI
{
    /// <summary>
    /// Translates between the RichTextBox's FlowDocument and the note-*.html
    /// file saved to disk. It's a custom dialect (well-formed XHTML-like
    /// XML, opens fine in a browser for manual inspection), not a full HTML
    /// engine: it only needs to understand what the editor itself writes, so
    /// XDocument is enough instead of a third-party HTML parser.
    ///
    /// Supported tags: p, h1, h2, b, i, s, a, ul/ol/li, img, table/tr/td.
    /// </summary>
    public static class HtmlDocConverter
    {
        // ================= FlowDocument -> HTML =================

        public static (string html, List<string> imageIds) ToHtml(FlowDocument doc, string gameDir)
        {
            var imageIds = new List<string>();
            var root = new XElement("body");

            foreach (var block in doc.Blocks)
            {
                root.Add(BlockToXml(block, imageIds));
            }

            var xdoc = new XDocument(new XElement("gamenote", root));
            return (xdoc.ToString(SaveOptions.DisableFormatting), imageIds);
        }

        private static XElement BlockToXml(Block block, List<string> imageIds)
        {
            switch (block)
            {
                case Paragraph p when IsHeading(p, out int level):
                    return new XElement(level == 1 ? "h1" : "h2", InlinesToXml(p.Inlines));

                case Paragraph p:
                    return new XElement("p", InlinesToXml(p.Inlines));

                case List list:
                    var tag = list.MarkerStyle == TextMarkerStyle.Decimal ? "ol" : "ul";
                    var listEl = new XElement(tag);
                    foreach (var item in list.ListItems)
                    {
                        var liEl = new XElement("li");
                        foreach (var innerBlock in item.Blocks)
                        {
                            liEl.Add(BlockToXml(innerBlock, imageIds));
                        }
                        listEl.Add(liEl);
                    }
                    return listEl;

                case BlockUIContainer buc when buc.Child is System.Windows.Controls.Image img:
                    var relPath = (img.Tag as string) ?? "";
                    var imgId = Path.GetFileNameWithoutExtension(relPath);
                    if (!string.IsNullOrEmpty(imgId)) imageIds.Add(imgId);
                    return new XElement("img",
                        new XAttribute("src", relPath.Replace('\\', '/')),
                        new XAttribute("data-image-id", imgId));

                case BlockUIContainer buc when buc.Child is TableGridControl grid:
                    var tableEl = new XElement("table", new XAttribute("data-gamenotes-table", "1"));
                    foreach (var row in grid.ExportRaw())
                    {
                        var trEl = new XElement("tr");
                        foreach (var cell in row)
                        {
                            trEl.Add(new XElement("td", cell ?? ""));
                        }
                        tableEl.Add(trEl);
                    }
                    return tableEl;

                default:
                    // Unrecognized block: keep it as a plain-text paragraph
                    // instead of silently losing the information.
                    var range = new TextRange(block.ContentStart, block.ContentEnd);
                    return new XElement("p", range.Text);
            }
        }

        private static bool IsHeading(Paragraph p, out int level)
        {
            level = 0;
            var run = p.Inlines.OfType<Run>().FirstOrDefault();
            double size = run?.FontSize ?? p.FontSize;
            if (size >= 20) { level = 1; return true; }
            if (size >= 16) { level = 2; return true; }
            return false;
        }

        private static IEnumerable<XNode> InlinesToXml(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                yield return InlineToXml(inline);
            }
        }

        private static XNode InlineToXml(Inline inline)
        {
            switch (inline)
            {
                case Run run:
                    XNode node = new XText(run.Text);
                    if (run.FontWeight == FontWeights.Bold) node = new XElement("b", node);
                    if (run.FontStyle == FontStyles.Italic) node = new XElement("i", node);
                    if (run.TextDecorations != null && run.TextDecorations.Count > 0 &&
                        run.TextDecorations.Contains(TextDecorations.Strikethrough[0]))
                    {
                        node = new XElement("s", node);
                    }
                    return node;

                case Hyperlink link:
                    var text = new TextRange(link.ContentStart, link.ContentEnd).Text;
                    return new XElement("a", new XAttribute("href", link.NavigateUri?.ToString() ?? "#"), text);

                default:
                    var tr = new TextRange(inline.ContentStart, inline.ContentEnd);
                    return new XText(tr.Text);
            }
        }

        // ================= HTML -> FlowDocument =================

        public static FlowDocument FromHtml(string html, string gameDir)
        {
            var doc = new FlowDocument();

            if (string.IsNullOrWhiteSpace(html))
            {
                doc.Blocks.Add(new Paragraph());
                return doc;
            }

            XElement root;
            try
            {
                // Compatibility with older/plain initial content.
                if (!html.TrimStart().StartsWith("<gamenote"))
                {
                    doc.Blocks.Add(new Paragraph(new Run(StripTags(html))));
                    return doc;
                }
                root = XElement.Parse(html);
            }
            catch (Exception)
            {
                // Never throw upward; show the raw text as a last resort
                // instead of losing the note.
                doc.Blocks.Add(new Paragraph(new Run(html)));
                return doc;
            }

            var body = root.Element("body") ?? root;
            foreach (var el in body.Elements())
            {
                var block = XmlToBlock(el, gameDir);
                if (block != null) doc.Blocks.Add(block);
            }

            if (!doc.Blocks.Any())
            {
                doc.Blocks.Add(new Paragraph());
            }

            return doc;
        }

        private static string StripTags(string s) => System.Text.RegularExpressions.Regex.Replace(s, "<.*?>", "");

        private static Block XmlToBlock(XElement el, string gameDir)
        {
            switch (el.Name.LocalName)
            {
                case "h1":
                    return new Paragraph(BuildInlines(el)) { FontSize = 22, FontWeight = FontWeights.Bold };

                case "h2":
                    return new Paragraph(BuildInlines(el)) { FontSize = 17, FontWeight = FontWeights.Bold };

                case "p":
                    return new Paragraph(BuildInlines(el));

                case "ul":
                case "ol":
                    var list = new List
                    {
                        MarkerStyle = el.Name.LocalName == "ol" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc
                    };
                    foreach (var liEl in el.Elements("li"))
                    {
                        var listItem = new ListItem();
                        var innerBlocks = liEl.Elements().Any()
                            ? liEl.Elements().Select(e2 => XmlToBlock(e2, gameDir)).Where(b => b != null)
                            : new[] { new Paragraph(new Run(liEl.Value)) };
                        foreach (var b in innerBlocks) listItem.Blocks.Add(b);
                        list.ListItems.Add(listItem);
                    }
                    return list;

                case "img":
                    return BuildImageBlock(el, gameDir);

                case "table":
                    return BuildTableBlock(el);

                default:
                    return new Paragraph(new Run(el.Value));
            }
        }

        private static IEnumerable<Inline> BuildInlines(XElement el)
        {
            foreach (var node in el.Nodes())
            {
                if (node is XText text)
                {
                    yield return new Run(text.Value);
                }
                else if (node is XElement child)
                {
                    yield return BuildInlineFromElement(child);
                }
            }

            // If the <p> is empty (no nodes), at least emit an empty Run so
            // the paragraph exists and can be typed into.
            if (!el.Nodes().Any())
            {
                yield return new Run("");
            }
        }

        private static Inline BuildInlineFromElement(XElement el)
        {
            switch (el.Name.LocalName)
            {
                case "b":
                    return WrapStyle(el, run => run.FontWeight = FontWeights.Bold);
                case "i":
                    return WrapStyle(el, run => run.FontStyle = FontStyles.Italic);
                case "s":
                    return WrapStyle(el, run => run.TextDecorations = TextDecorations.Strikethrough);
                case "a":
                    var link = new Hyperlink(new Run(el.Value));
                    if (Uri.TryCreate(el.Attribute("href")?.Value, UriKind.Absolute, out var uri))
                    {
                        link.NavigateUri = uri;
                        link.RequestNavigate += (s, args) =>
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(args.Uri.ToString())
                                { UseShellExecute = true });
                            }
                            catch { /* invalid link: ignored without breaking the note */ }
                        };
                    }
                    return link;
                default:
                    return new Run(el.Value);
            }
        }

        private static Run WrapStyle(XElement el, Action<Run> apply)
        {
            // May nest more than one style (e.g. <b><i>text</i></b>): walk
            // down applying each modifier onto the same Run.
            XElement current = el;
            var stack = new Stack<XElement>();
            while (true)
            {
                stack.Push(current);
                var onlyChild = current.Elements().Count() == 1 && !current.Nodes().OfType<XText>().Any() ? current.Elements().First() : null;
                if (onlyChild != null && (onlyChild.Name.LocalName == "b" || onlyChild.Name.LocalName == "i" || onlyChild.Name.LocalName == "s"))
                {
                    current = onlyChild;
                }
                else break;
            }

            var run = new Run(current.Value);
            foreach (var node in stack)
            {
                if (node.Name.LocalName == "b") run.FontWeight = FontWeights.Bold;
                if (node.Name.LocalName == "i") run.FontStyle = FontStyles.Italic;
                if (node.Name.LocalName == "s") run.TextDecorations = TextDecorations.Strikethrough;
            }
            apply(run);
            return run;
        }

        private static Block BuildImageBlock(XElement el, string gameDir)
        {
            var src = el.Attribute("src")?.Value ?? "";
            var fullPath = Path.Combine(gameDir, src.Replace('/', Path.DirectorySeparatorChar));

            try
            {
                if (!File.Exists(fullPath))
                {
                    return new Paragraph(new Run($"[Image not found: {src}]"));
                }

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(fullPath, UriKind.Absolute);
                bitmap.EndInit();

                var image = new System.Windows.Controls.Image
                {
                    Source = bitmap,
                    MaxWidth = 480,
                    Stretch = Stretch.Uniform,
                    Tag = src,
                };
                return new BlockUIContainer(image);
            }
            catch (Exception)
            {
                // A damaged image must not take down the rest of the note.
                return new Paragraph(new Run($"[Could not load image: {src}]"));
            }
        }

        private static Block BuildTableBlock(XElement el)
        {
            var rows = new List<List<string>>();
            foreach (var tr in el.Elements("tr"))
            {
                rows.Add(tr.Elements("td").Select(td => td.Value).ToList());
            }
            if (rows.Count == 0) rows.Add(new List<string> { "" });

            var grid = new TableGridControl();
            grid.LoadFrom(rows);
            return new BlockUIContainer(grid);
        }
    }
}
