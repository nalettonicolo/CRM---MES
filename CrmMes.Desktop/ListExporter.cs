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

    /// <summary>Certificato di conformità per una commessa: elenco delle misurazioni registrate contro
    /// il piano di controllo del prodotto, con esito pass/fail per ciascuna e complessivo.</summary>
    public static void ExportQualityCertificate(QualityCertificateDto certificate, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Text("Certificato di conformità").FontSize(20).Bold();
                    column.Item().PaddingTop(6).Text($"Commessa {certificate.WorkOrderCode} — Lotto {certificate.ProductLotNumber}").FontSize(12);
                    column.Item().Text($"{certificate.ProductCode} — {certificate.ProductName}").FontSize(11).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(4).Text($"Emesso il {DateTime.Now:dd/MM/yyyy HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                    column.Item().PaddingTop(10).Background(certificate.AllPassed ? Colors.Green.Lighten4 : Colors.Red.Lighten4)
                        .Padding(8).Text(certificate.AllPassed ? "ESITO: CONFORME" : "ESITO: NON CONFORME")
                        .FontSize(13).Bold().FontColor(certificate.AllPassed ? Colors.Green.Darken2 : Colors.Red.Darken2);
                    column.Item().PaddingTop(10).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(10).Table(table =>
                {
                    table.ColumnsDefinition(columnsDef =>
                    {
                        columnsDef.RelativeColumn(2);
                        columnsDef.RelativeColumn(1.5f);
                        columnsDef.RelativeColumn(1);
                        columnsDef.RelativeColumn(1);
                        columnsDef.RelativeColumn(1);
                        columnsDef.RelativeColumn(1);
                    });

                    table.Header(header =>
                    {
                        foreach (var text in new[] { "Caratteristica", "Unità", "Min", "Max", "Misurato", "Esito" })
                        {
                            header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Text(text).Bold();
                        }
                    });

                    foreach (var measurement in certificate.Measurements)
                    {
                        var outcome = measurement.IsWithinTolerance switch
                        {
                            true => "Conforme",
                            false => "Non conforme",
                            null => "—"
                        };
                        var color = measurement.IsWithinTolerance switch
                        {
                            true => Colors.Green.Darken2,
                            false => Colors.Red.Darken2,
                            null => Colors.Grey.Darken1
                        };

                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(measurement.CheckpointName);
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(measurement.Unit ?? "-");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(measurement.LowerLimit?.ToString("0.###") ?? "-");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(measurement.UpperLimit?.ToString("0.###") ?? "-");
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(measurement.MeasuredValue.ToString("0.###"));
                        table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(outcome).FontColor(color).Bold();
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

    /// <summary>Preventivo da inviare al cliente: intestazione con cliente e validità, righe con
    /// quantità/prezzo/sconto/totale, totale imponibile in fondo. Importi IVA esclusa: l'IVA la
    /// calcola il gestionale fiscale in fattura.</summary>
    public static void ExportQuote(QuoteDto quote, CustomerDto? customer, string filePath)
    {
        var euro = System.Globalization.CultureInfo.GetCultureInfo("it-IT");

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(10));

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Preventivo").FontSize(22).Bold();
                            left.Item().Text(quote.Code).FontSize(11).FontColor(Colors.Grey.Darken1);
                            left.Item().PaddingTop(4).Text($"Data {quote.CreatedAt.ToLocalTime():dd/MM/yyyy}").FontSize(10);
                            if (quote.ValidUntil.HasValue)
                            {
                                left.Item().Text($"Valido fino al {quote.ValidUntil.Value:dd/MM/yyyy}").FontSize(10);
                            }
                        });
                        row.ConstantItem(220).Column(right =>
                        {
                            right.Item().Text("Spett.le").FontSize(9).FontColor(Colors.Grey.Darken1);
                            right.Item().Text(quote.CustomerName).FontSize(12).Bold();
                            if (!string.IsNullOrWhiteSpace(customer?.Address))
                            {
                                right.Item().Text(customer.Address);
                            }

                            if (!string.IsNullOrWhiteSpace(customer?.VatNumber))
                            {
                                right.Item().Text($"P.IVA {customer.VatNumber}");
                            }
                        });
                    });
                    column.Item().PaddingTop(14).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                });

                page.Content().PaddingTop(12).Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columnsDef =>
                        {
                            columnsDef.RelativeColumn(4);
                            columnsDef.RelativeColumn(1);
                            columnsDef.RelativeColumn(1.4f);
                            columnsDef.RelativeColumn(1);
                            columnsDef.RelativeColumn(1.5f);
                        });

                        table.Header(header =>
                        {
                            header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Text("Descrizione").Bold();
                            foreach (var text in new[] { "Q.tà", "Prezzo €", "Sconto", "Totale €" })
                            {
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).AlignRight().Text(text).Bold();
                            }
                        });

                        foreach (var item in quote.Items)
                        {
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).Column(cell =>
                            {
                                cell.Item().Text(item.Description);
                                if (item.ProductCode is not null)
                                {
                                    cell.Item().Text($"Cod. {item.ProductCode}").FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                            });
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text(item.Quantity.ToString("0.##", euro));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text(item.UnitPrice.ToString("N2", euro));
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight()
                                .Text(item.DiscountPercent > 0 ? $"{item.DiscountPercent.ToString("0.##", euro)}%" : "-");
                            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5).AlignRight().Text(item.LineTotal.ToString("N2", euro));
                        }
                    });

                    content.Item().PaddingTop(12).AlignRight().Text($"Totale imponibile {quote.Total.ToString("N2", euro)} €").FontSize(13).Bold();
                    content.Item().AlignRight().Text("Importi IVA esclusa.").FontSize(9).FontColor(Colors.Grey.Darken1);

                    if (!string.IsNullOrWhiteSpace(quote.Notes))
                    {
                        content.Item().PaddingTop(20).Text("Note").Bold();
                        content.Item().Text(quote.Notes);
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

    private static string Sanitize(string sheetName)
    {
        var invalid = new[] { '\\', '/', '?', '*', '[', ']', ':' };
        var clean = new string(sheetName.Where(c => !invalid.Contains(c)).ToArray());
        return clean.Length > 31 ? clean[..31] : (clean.Length == 0 ? "Foglio1" : clean);
    }
}
