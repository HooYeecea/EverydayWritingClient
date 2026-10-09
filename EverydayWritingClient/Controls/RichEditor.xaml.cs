using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using EverydayWritingClient.Services;

namespace EverydayWritingClient.Controls;

public partial class RichEditor : UserControl
{
    public event EventHandler? ContentChanged;

    public RichEditor()
    {
        InitializeComponent();
        Editor.Document.FontFamily = new System.Windows.Media.FontFamily("Georgia");
        Editor.Document.FontSize = 17;
        Editor.Document.PagePadding = new Thickness(0);
    }

    public string Html
    {
        get => RichTextHtml.Save(Editor.Document);
        set => RichTextHtml.Load(Editor.Document, value);
    }

    public string PlainText => RichTextHtml.PlainText(Editor.Document);

    public void SetFontSize(double size)
    {
        Editor.FontSize = size;
        Editor.Document.FontSize = size;
    }

    private void EditorTextChanged(object sender, TextChangedEventArgs e) => ContentChanged?.Invoke(this, EventArgs.Empty);

    private void BoldClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleBold.Execute(null, Editor);
    private void ItalicClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleItalic.Execute(null, Editor);
    private void UnderlineClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleUnderline.Execute(null, Editor);
    private void ListClick(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, Editor);

    private void BodyClick(object sender, RoutedEventArgs e) => ApplyBlock(null, Editor.Document.FontSize, FontWeights.Normal);
    private void H1Click(object sender, RoutedEventArgs e) => ApplyBlock("h1", 28, FontWeights.SemiBold);
    private void H2Click(object sender, RoutedEventArgs e) => ApplyBlock("h2", 22, FontWeights.SemiBold);
    private void H3Click(object sender, RoutedEventArgs e) => ApplyBlock("h3", 18, FontWeights.SemiBold);

    private void QuoteClick(object sender, RoutedEventArgs e)
    {
        foreach (var paragraph in SelectedParagraphs())
        {
            paragraph.Tag = "quote";
            paragraph.Margin = new Thickness(12, 4, 0, 8);
        }
    }

    private void ApplyBlock(string? tag, double size, FontWeight weight)
    {
        foreach (var paragraph in SelectedParagraphs())
        {
            paragraph.Tag = tag;
            paragraph.FontSize = size;
            paragraph.FontWeight = weight;
            paragraph.Margin = new Thickness(0, 0, 0, 8);
        }
    }

    private IEnumerable<Paragraph> SelectedParagraphs()
    {
        var start = Editor.Selection.Start;
        var end = Editor.Selection.End;
        foreach (var block in Enumerate(Editor.Document.Blocks))
        {
            if (block is not Paragraph paragraph)
            {
                continue;
            }

            var overlaps = paragraph.ContentEnd.CompareTo(start) >= 0 && paragraph.ContentStart.CompareTo(end) <= 0;
            if (overlaps || (start.CompareTo(end) == 0 && start.Paragraph == paragraph))
            {
                yield return paragraph;
            }
        }
    }

    private static IEnumerable<Block> Enumerate(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            if (block is FlowList list)
            {
                foreach (var item in list.ListItems)
                {
                    foreach (var child in Enumerate(item.Blocks))
                    {
                        yield return child;
                    }
                }
            }
        }
    }
}
