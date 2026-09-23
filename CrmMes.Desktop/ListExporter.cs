using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CrmMes.Desktop;

/// <summary>One exportable column: a header label and how to read that column's text out of a row
/// object. Reflection-free and DTO-agnostic on purpose, so every screen's "Esporta" button can reuse
/// this instead of hand-writing an Excel/PDF layout per list.</summary>
public sealed record ExportColumn(string Header, Func<object, string> Value);

/// <summary>Exports whatever is currently on screen (a ListView's bound rows) to .xlsx or .pdf. Not a
/// report designer — one flat table per export, matching what the user is already looking at.</summary>
public static class ListExporter
{
    // Set here (not just in App's constructor) so exporting also works correctly from the test host,
    // which never runs App's startup code.
    static ListExporter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static void ExportToExcel(string title, IReadOnlyList<ExportColumn> columns, IEnumerable<object> rows, string filePath)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(Sanitize(title));

        for (var i = 0; i < columns.Count; i++)
        {
            sheet.Cell(1, i + 1).Value = columns[i].Header;
            sheet.Cell(1, i + 1).Style.Font.Bold = true;
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                sheet.Cell(rowIndex, i + 1).Value = columns[i].Value(row);
            }

            rowIndex++;
        }

        sheet.Columns().AdjustToContents();
        workbook.SaveAs(filePath);
    }

    public static void ExportToPdf(string title, IReadOnlyList<ExportColumn> columns, IEnumerable<object> rows, string filePath)
    {
        var materialized = rows.ToList();

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(style => style.FontSize(9));

                page.Header().Column(column =>
                {
                    column.Item().Text(title).FontSize(18).Bold();
                    column.Item().Text($"Esportato il {DateTime.Now:dd/MM/yyyy HH:mm} — {materialized.Count} righe").FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(8).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columnsDef =>
                    {
                        foreach (var _ in columns)
                        {
                            columnsDef.RelativeColumn();
                        }
                    });

                    table.Header(header =>
                    {
                        foreach (var column in columns)
                        {
                            header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4)
                                .Text(column.Header).Bold();
                        }
                    });

                    foreach (var row in materialized)
                    {
                        foreach (var column in columns)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4)
                                .Text(column.Value(row));
                        }
                    }
                });

                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>A small printable label (QR + code/product/lot) for a work order traveler — attach it to
    /// the physical batch so <see cref="ShopFloorTerminalWindow"/> can scan it back at the machine.</summary>
    public static void ExportWorkOrderLabel(string code, string productName, string lotNumber, byte[] qrPngBytes, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(340, 420);
                page.Margin(20);
                page.Content().Column(column =>
                {
                    column.Item().AlignCenter().Height(220).Image(qrPngBytes);
                    column.Item().PaddingTop(14).AlignCenter().Text(code).FontSize(16).Bold();
                    column.Item().PaddingTop(4).AlignCenter().Text(productName).FontSize(12);
                    column.Item().AlignCenter().Text($"Lotto {lotNumber}").FontSize(10).FontColor(Colors.Grey.Darken1);
                });
            });
        }).GeneratePdf(filePath);
    }

    private static string Sanitize(string sheetName)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(sheetName.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 31 ? clean[..31] : (clean.Length == 0 ? "Foglio1" : clean);
    }
}
