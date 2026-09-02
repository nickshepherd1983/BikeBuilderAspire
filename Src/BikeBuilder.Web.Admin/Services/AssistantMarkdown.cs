using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace BikeBuilder.Web.Admin.Services;

// Renders the assistant's Markdown replies to HTML for the chat window. Raw HTML in the
// model's output is disabled (it is untrusted text), and any table column whose body cells
// are all numbers - prices, counts, ratings - is right-aligned so figures line up, since
// models rarely bother with alignment markers themselves.
public static class AssistantMarkdown
{
  static readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
      .UsePipeTables()
      .UseGridTables()
      .UseListExtras()
      .UseEmphasisExtras()
      .UseAutoLinks()
      .DisableHtml()
      .Build();

  public static MarkupString Render(string markdown)
  {
    if (string.IsNullOrWhiteSpace(markdown))
      return new MarkupString("");

    var document = Markdown.Parse(markdown, _pipeline);
    foreach (var table in document.Descendants<Table>())
      AlignNumericColumns(table);

    using var writer = new StringWriter();
    var renderer = new HtmlRenderer(writer);
    _pipeline.Setup(renderer);
    renderer.Render(document);
    return new MarkupString(writer.ToString());
  }

  static void AlignNumericColumns(Table table)
  {
    var rows = table.OfType<TableRow>().Where(row => !row.IsHeader).ToList();
    for (var column = 0; column < table.ColumnDefinitions.Count; column++)
    {
      var texts = rows
          .Select(row => row.ElementAtOrDefault(column) as TableCell)
          .Where(cell => cell is not null)
          .Select(cell => CellText(cell!).Trim())
          .Where(text => text.Length > 0 && text != "-" && text != "—")
          .ToList();

      if (texts.Count > 0 && texts.All(LooksNumeric))
        table.ColumnDefinitions[column].Alignment = TableColumnAlign.Right;
    }
  }

  static string CellText(TableCell cell) =>
      string.Concat(cell.Descendants<LiteralInline>().Select(literal => literal.Content.ToString()));

  // "1,234.50", "$899.99", "4.75 ★", "12%", "-3" all count; anything with letters does not,
  // and neither do dates or times ("09/02/2026 14:30"), which stay left-aligned.
  static bool LooksNumeric(string text)
  {
    if (text.Any(char.IsLetter) || text.Contains('/') || text.Contains(':'))
      return false;

    var stripped = new string(text.Where(c => char.IsDigit(c) || c is '.' or '-' or '+').ToArray());
    return stripped.Length > 0
        && decimal.TryParse(stripped, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
  }
}
