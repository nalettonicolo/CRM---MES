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

    /// <summary>Base text style of every PDF: no typographic ligatures. The embedded font draws "ff", "fi",
    /// "fb", "tt"... as single glyphs that carry no letters, so copying a code or a lot number out of a
    /// PDF (or a supplier reading it with a text tool) would lose them: "L-2026-ffb1" came out as "L-2026- 1".</summary>
    private static TextStyle Plain(TextStyle style) => style.DisableFontFeature(FontFeatures.StandardLigatures);

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
                page.DefaultTextStyle(style => Plain(style).FontSize(9));

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
                page.DefaultTextStyle(Plain);
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
                page.DefaultTextStyle(style => Plain(style).FontSize(10));

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
                page.DefaultTextStyle(style => Plain(style).FontSize(10));

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

    /// <summary>Documento di trasporto (DPR 472/1996): mittente from the company profile, destinatario and
    /// destination, causale, goods with quantities, who transports, appearance/packages/weight, start of
    /// transport and signature boxes. Drafts print as "BOZZA" and cancelled documents as "ANNULLATO", so
    /// neither can pass for a valid document.</summary>
    public static void ExportTransportDocument(TransportDocumentDto document, CompanyProfileDto? company, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        var stamp = document.Status switch
        {
            "Draft" => "BOZZA - NON VALIDO COME DDT",
            "Cancelled" => "ANNULLATO",
            _ => null
        };

        static void Box(IContainer container, string label, string? value) =>
            container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(6).Column(column =>
            {
                column.Item().Text(label).FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                column.Item().Text(string.IsNullOrWhiteSpace(value) ? " " : value).FontSize(10);
            });

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => Plain(style).FontSize(9.5f));

                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text("Mittente").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                            left.Item().Text(company?.CompanyName is { Length: > 0 } name ? name : "(configura i dati azienda)").FontSize(13).Bold();
                            if (!string.IsNullOrWhiteSpace(company?.Address))
                            {
                                left.Item().Text(company.Address);
                            }

                            if (!string.IsNullOrWhiteSpace(company?.VatNumber))
                            {
                                left.Item().Text($"P.IVA {company.VatNumber}");
                            }

                            var contacts = string.Join("  ·  ", new[] { company?.Phone, company?.Email }.Where(v => !string.IsNullOrWhiteSpace(v)));
                            if (contacts.Length > 0)
                            {
                                left.Item().Text(contacts).FontSize(8.5f).FontColor(Colors.Grey.Darken1);
                            }
                        });
                        row.ConstantItem(230).AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text("DOCUMENTO DI TRASPORTO").FontSize(13).Bold();
                            right.Item().AlignRight().Text("D.P.R. 472/1996").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                            right.Item().PaddingTop(6).AlignRight().Text(document.Number is { } number
                                ? $"N. {number}/{document.Year} del {(document.IssuedAt ?? document.CreatedAt).ToLocalTime():dd/MM/yyyy}"
                                : "Numero assegnato all'emissione").FontSize(11).Bold();
                            if (document.WorkOrderCode is not null)
                            {
                                right.Item().AlignRight().Text($"Rif. commessa {document.WorkOrderCode}").FontSize(8.5f);
                            }
                        });
                    });
                    if (stamp is not null)
                    {
                        column.Item().PaddingTop(8).Border(1.5f).BorderColor(Colors.Red.Darken2).Padding(4).AlignCenter()
                            .Text(stamp).FontSize(14).Bold().FontColor(Colors.Red.Darken2);
                    }
                });

                page.Content().PaddingTop(12).Column(content =>
                {
                    content.Item().Row(row =>
                    {
                        row.RelativeItem().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(recipient =>
                        {
                            recipient.Item().Text("Destinatario").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                            recipient.Item().Text(document.RecipientName).FontSize(11).Bold();
                            if (!string.IsNullOrWhiteSpace(document.RecipientAddress))
                            {
                                recipient.Item().Text(document.RecipientAddress);
                            }

                            if (!string.IsNullOrWhiteSpace(document.RecipientVatNumber))
                            {
                                recipient.Item().Text($"P.IVA {document.RecipientVatNumber}");
                            }
                        });
                        row.ConstantItem(10);
                        row.RelativeItem().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(destination =>
                        {
                            destination.Item().Text("Luogo di destinazione").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                            destination.Item().Text(document.DestinationAddress ?? document.RecipientAddress ?? "Sede del destinatario");
                        });
                    });

                    content.Item().PaddingTop(8).Row(row =>
                    {
                        row.RelativeItem(2).Element(c => Box(c, "Causale del trasporto", document.ReasonLabel));
                        row.RelativeItem().Element(c => Box(c, "Trasporto a cura del", TransportDocumentDto.TransportByText(document.TransportBy)));
                        row.RelativeItem(2).Element(c => Box(c, "Vettore", document.CarrierName));
                        row.RelativeItem().Element(c => Box(c, "Porto", document.Port));
                    });

                    content.Item().PaddingTop(12).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(28);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(4.6f);
                            columns.ConstantColumn(40);
                            columns.ConstantColumn(60);
                        });

                        table.Header(header =>
                        {
                            foreach (var (text, right) in new[] { ("#", false), ("Codice", false), ("Descrizione dei beni (natura e qualità)", false), ("U.m.", false), ("Quantità", true) })
                            {
                                var cell = header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4);
                                (right ? cell.AlignRight() : cell).Text(text).Bold().FontSize(8.5f);
                            }
                        });

                        foreach (var line in document.Lines)
                        {
                            IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4);
                            Cell().Text(line.LineNumber.ToString(italian)).FontColor(Colors.Grey.Darken1);
                            Cell().Text(line.Code ?? string.Empty);
                            Cell().Column(cell =>
                            {
                                cell.Item().Text(line.Description);
                                var extra = string.Join("  ·  ", new[]
                                {
                                    line.LotNumber is null ? null : $"Lotto {line.LotNumber}",
                                    line.Notes
                                }.Where(v => !string.IsNullOrWhiteSpace(v)));
                                if (extra.Length > 0)
                                {
                                    cell.Item().Text(extra).FontSize(8).FontColor(Colors.Grey.Darken1);
                                }
                            });
                            Cell().Text(line.Unit);
                            Cell().AlignRight().Text(line.Quantity.ToString("0.###", italian));
                        }
                    });

                    content.Item().PaddingTop(12).Row(row =>
                    {
                        row.RelativeItem(2).Element(c => Box(c, "Aspetto esteriore dei beni", document.GoodsAppearance));
                        row.RelativeItem().Element(c => Box(c, "N. colli", document.Packages?.ToString(italian)));
                        row.RelativeItem().Element(c => Box(c, "Peso lordo kg", document.GrossWeightKg?.ToString("0.###", italian)));
                        row.RelativeItem(2).Element(c => Box(c, "Data e ora inizio trasporto",
                            document.TransportStartAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", italian)));
                    });

                    if (document.IsSubcontracting)
                    {
                        content.Item().PaddingTop(8).Text(text =>
                        {
                            text.Span("Merce inviata in conto lavorazione, da restituire lavorata.").Bold();
                            if (document.ExpectedReturnAt is { } expected)
                            {
                                text.Span($" Rientro previsto entro il {expected.ToLocalTime():dd/MM/yyyy}.");
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(document.Notes))
                    {
                        content.Item().PaddingTop(8).Text("Annotazioni").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        content.Item().Text(document.Notes);
                    }

                    if (document.Status == "Cancelled" && document.CancellationReason is not null)
                    {
                        content.Item().PaddingTop(8).Text($"Annullato il {document.CancelledAt?.ToLocalTime():dd/MM/yyyy}: {document.CancellationReason}")
                            .FontColor(Colors.Red.Darken2);
                    }

                    content.Item().PaddingTop(28).Row(row =>
                    {
                        foreach (var label in new[] { "Firma del conducente", "Firma del vettore", "Firma del destinatario" })
                        {
                            row.RelativeItem().PaddingHorizontal(6).Column(signature =>
                            {
                                signature.Item().Height(28);
                                signature.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);
                                signature.Item().PaddingTop(2).AlignCenter().Text(label).FontSize(8).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    });
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

    /// <summary>Declaration of conformity of a low-voltage assembly (CEI EN 61439, Low Voltage Directive
    /// 2014/35/UE and EMC Directive 2014/30/UE) with its rated data, followed by the routine verification
    /// report of clause 11. Only printed for a completed verification.</summary>
    public static void ExportPanelDeclaration(PanelVerificationDto verification, CompanyProfileDto? company, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        string Value(decimal? value, string unit) => value is { } number ? $"{number.ToString("0.###", italian)} {unit}" : "-";
        var companyName = company?.CompanyName is { Length: > 0 } name ? name : "(configura i dati azienda)";

        void Header(IContainer container, string title) => container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(companyName).FontSize(12).Bold();
                    if (!string.IsNullOrWhiteSpace(company?.Address))
                    {
                        left.Item().Text(company.Address).FontSize(9);
                    }

                    if (!string.IsNullOrWhiteSpace(company?.VatNumber))
                    {
                        left.Item().Text($"P.IVA {company.VatNumber}").FontSize(9);
                    }
                });
                row.ConstantItem(240).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(title).FontSize(13).Bold();
                    right.Item().AlignRight().Text($"Commessa {verification.WorkOrderCode}").FontSize(9);
                });
            });
            column.Item().PaddingTop(10).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
        });

        static void DataRow(TableDescriptor table, string label, string value)
        {
            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(label).FontColor(Colors.Grey.Darken2);
            table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(4).Text(value).Bold();
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => Plain(style).FontSize(10));
                page.Header().Element(c => Header(c, "DICHIARAZIONE DI CONFORMITÀ"));
                page.Content().PaddingTop(14).Column(content =>
                {
                    content.Item().Text(text =>
                    {
                        text.Span("Il costruttore del quadro ");
                        text.Span(companyName).Bold();
                        text.Span(" dichiara sotto la propria responsabilità che l'apparecchiatura assiemata di protezione e manovra per bassa tensione descritta di seguito è conforme alla norma ");
                        text.Span(verification.Standard).Bold();
                        text.Span(" (in combinazione con la CEI EN 61439-1) e alle direttive 2014/35/UE (Bassa Tensione) e 2014/30/UE (Compatibilità Elettromagnetica), ed è stata sottoposta con esito positivo alla verifica individuale prevista dalla norma.");
                    });

                    content.Item().PaddingTop(14).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(2);
                            columns.RelativeColumn(3);
                        });
                        DataRow(table, "Quadro", $"{verification.ProductCode} {verification.ProductName}");
                        DataRow(table, "Matricola", verification.SerialNumber ?? verification.ProductLotNumber ?? "-");
                        DataRow(table, "Cliente", verification.CustomerName ?? "-");
                        DataRow(table, "Sistema / costruttore originale", string.Join(" · ", new[] { verification.SystemReference, verification.OriginalManufacturer }.Where(v => !string.IsNullOrWhiteSpace(v))) is { Length: > 0 } system ? system : "-");
                        DataRow(table, "Tensione nominale Un", Value(verification.RatedVoltage, "V"));
                        DataRow(table, "Corrente nominale del quadro InA", Value(verification.RatedCurrent, "A"));
                        DataRow(table, "Frequenza nominale", Value(verification.RatedFrequency, "Hz"));
                        DataRow(table, "Corrente di breve durata Icw (1 s)", Value(verification.ShortTimeWithstandCurrent, "kA"));
                        DataRow(table, "Corrente di cortocircuito condizionata Icc", Value(verification.ConditionalShortCircuitCurrent, "kA"));
                        DataRow(table, "Grado di protezione", verification.IpRating ?? "-");
                        DataRow(table, "Forma di segregazione", verification.InternalSeparation ?? "-");
                        DataRow(table, "Sistema di distribuzione", verification.EarthingSystem ?? "-");
                    });

                    content.Item().PaddingTop(18).Text($"Data {verification.CompletedAt?.ToLocalTime():dd/MM/yyyy}");
                    content.Item().PaddingTop(30).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(220).Column(signature =>
                        {
                            signature.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);
                            signature.Item().PaddingTop(2).AlignCenter().Text("Il legale rappresentante / responsabile tecnico").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });
                page.Footer().AlignCenter().Text("Allegato: rapporto di verifica individuale").FontSize(8).FontColor(Colors.Grey.Darken1);
            });

            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => Plain(style).FontSize(10));
                page.Header().Element(c => Header(c, "RAPPORTO DI VERIFICA INDIVIDUALE"));
                page.Content().PaddingTop(14).Column(content =>
                {
                    content.Item().Text($"CEI EN 61439-1, articolo 11 · {verification.Standard}").FontColor(Colors.Grey.Darken2);
                    content.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(45);
                            columns.RelativeColumn(4);
                            columns.RelativeColumn(1.4f);
                            columns.RelativeColumn(2);
                        });
                        table.Header(header =>
                        {
                            foreach (var text in new[] { "Punto", "Verifica", "Esito", "Note" })
                            {
                                header.Cell().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Text(text).Bold();
                            }
                        });
                        foreach (var check in verification.Checks)
                        {
                            IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(5);
                            Cell().Text(check.Clause);
                            Cell().Text(check.Description);
                            Cell().Text(PanelVerificationDto.ResultText(check.Result));
                            Cell().Text(check.Notes ?? string.Empty).FontSize(9);
                        }
                    });

                    content.Item().PaddingTop(12).Text(text =>
                    {
                        text.Span("Resistenza d'isolamento misurata: ");
                        text.Span(Value(verification.InsulationResistanceMOhm, "MΩ")).Bold();
                        text.Span("   ·   Tensione di prova: ");
                        text.Span(Value(verification.DielectricTestVoltage, "V")).Bold();
                    });
                    if (!string.IsNullOrWhiteSpace(verification.Notes))
                    {
                        content.Item().PaddingTop(8).Text($"Note: {verification.Notes}");
                    }

                    content.Item().PaddingTop(14).Text($"Verifica eseguita da {verification.VerifiedBy} il {verification.CompletedAt?.ToLocalTime():dd/MM/yyyy HH:mm}.");
                    content.Item().PaddingTop(30).Row(row =>
                    {
                        row.RelativeItem();
                        row.ConstantItem(220).Column(signature =>
                        {
                            signature.Item().LineHorizontal(0.75f).LineColor(Colors.Grey.Darken1);
                            signature.Item().PaddingTop(2).AlignCenter().Text("Firma dell'addetto al collaudo").FontSize(8).FontColor(Colors.Grey.Darken1);
                        });
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>Food label of a produced lot (Reg. UE 1169/2011): legal name, ingredients in descending
    /// order of weight with allergens in bold, net quantity, date mark ("da consumarsi entro" for
    /// perishables, "da consumarsi preferibilmente entro" otherwise), lot, storage and producer. A6 page,
    /// the size of a common label printer roll.</summary>
    public static void ExportFoodLabel(FoodLabelDto label, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A6);
                page.Margin(16);
                page.DefaultTextStyle(style => Plain(style).FontSize(8.5f));
                page.Content().Column(column =>
                {
                    column.Spacing(5);
                    column.Item().Text(label.SalesName).FontSize(14).Bold();
                    column.Item().Text(text =>
                    {
                        text.Span("Ingredienti: ").Bold();
                        for (var i = 0; i < label.Ingredients.Count; i++)
                        {
                            var ingredient = label.Ingredients[i];
                            var span = text.Span(ingredient.Name);
                            if (ingredient.Allergens.Count > 0)
                            {
                                span.Bold().Underline();
                            }

                            text.Span(i < label.Ingredients.Count - 1 ? ", " : ".");
                        }
                    });
                    column.Item().Text(label.Allergens.Count == 0
                        ? "Allergeni: nessuno degli allergeni del Reg. UE 1169/2011."
                        : $"Contiene: {string.Join(", ", label.Allergens).ToLower(italian)}.").Bold();
                    if (!string.IsNullOrWhiteSpace(label.NetQuantity))
                    {
                        column.Item().Text($"Quantità netta: {label.NetQuantity}").FontSize(11).Bold();
                    }

                    if (label.ExpiryDate is { } expiry)
                    {
                        column.Item().Text($"{(label.UseByDate ? "Da consumarsi entro il" : "Da consumarsi preferibilmente entro il")} {expiry:dd/MM/yyyy}").FontSize(10).Bold();
                    }

                    column.Item().Text($"Lotto: {label.LotNumber ?? label.WorkOrderCode}").FontSize(10).Bold();
                    if (!string.IsNullOrWhiteSpace(label.StorageConditions))
                    {
                        column.Item().Text(label.StorageConditions);
                    }

                    column.Item().PaddingTop(4).Text(text =>
                    {
                        text.Span("Prodotto da ").FontColor(Colors.Grey.Darken2);
                        text.Span(label.ProducerName ?? "(configura i dati azienda)").Bold();
                        if (!string.IsNullOrWhiteSpace(label.ProducerAddress))
                        {
                            text.Span($", {label.ProducerAddress}");
                        }
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>GS1 logistic label of a pallet: SSCC in large type and as GS1-128 barcode (AI 00), plus a
    /// second barcode with best-before (15), count (37) and lot (10) when known. 100 x 150 mm.</summary>
    public static void ExportPalletLabel(LogisticUnitDto unit, CompanyProfileDto? company, string filePath)
    {
        var sscc = new[] { Gs1Barcode.SsccElement(unit.Sscc) };
        var content = new List<Gs1Barcode.Element>();
        if (unit.BestBefore is { } bestBefore)
        {
            content.Add(Gs1Barcode.BestBeforeElement(bestBefore));
        }

        if (unit.Quantity is { } quantity && quantity > 0 && quantity == Math.Round(quantity))
        {
            content.Add(Gs1Barcode.CountElement(quantity));
        }

        if (!string.IsNullOrWhiteSpace(unit.LotNumber) && unit.LotNumber.Trim().Length <= 20 && unit.LotNumber.All(c => c is >= ' ' and <= '~'))
        {
            content.Add(Gs1Barcode.LotElement(unit.LotNumber));
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(new PageSize(100, 150, Unit.Millimetre));
                page.Margin(6, Unit.Millimetre);
                page.DefaultTextStyle(style => Plain(style).FontSize(9));
                page.Content().Column(column =>
                {
                    column.Spacing(4);
                    column.Item().Text(company?.CompanyName ?? string.Empty).FontSize(10).Bold();
                    column.Item().LineHorizontal(1);
                    column.Item().Text("SSCC").FontSize(7).FontColor(Colors.Grey.Darken2);
                    column.Item().Text(unit.Sscc).FontSize(15).Bold();
                    if (unit.ProductName is not null)
                    {
                        column.Item().Text("CONTENUTO").FontSize(7).FontColor(Colors.Grey.Darken2);
                        column.Item().Text($"{unit.ProductCode} {unit.ProductName}").FontSize(10).Bold();
                    }

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("QUANTITÀ").FontSize(7).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(unit.Quantity?.ToString("0.###") ?? "-").FontSize(11).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("LOTTO").FontSize(7).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(unit.LotNumber ?? "-").FontSize(11).Bold();
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("TMC / SCADENZA").FontSize(7).FontColor(Colors.Grey.Darken2);
                            c.Item().Text(unit.BestBefore?.ToString("dd/MM/yyyy") ?? "-").FontSize(11).Bold();
                        });
                    });
                    column.Item().LineHorizontal(1);
                    if (content.Count > 0)
                    {
                        column.Item().Height(18, Unit.Millimetre).AlignCenter().Svg(Gs1Barcode.Svg(content)).FitArea();
                        column.Item().AlignCenter().Text(Gs1Barcode.HumanReadable(content)).FontSize(8);
                    }

                    column.Item().PaddingTop(4).Height(24, Unit.Millimetre).AlignCenter().Svg(Gs1Barcode.Svg(sscc)).FitArea();
                    column.Item().AlignCenter().Text(Gs1Barcode.HumanReadable(sscc)).FontSize(9).Bold();
                });
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>Recall report: what the suspect lot went into and who received it.</summary>
    public static void ExportRecall(RecallDto recall, CompanyProfileDto? company, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => Plain(style).FontSize(9.5f));
                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text(company?.CompanyName ?? string.Empty).Bold();
                        row.RelativeItem().AlignRight().Text($"Rapporto di richiamo · {DateTime.Now:dd/MM/yyyy HH:mm}").FontColor(Colors.Grey.Darken1);
                    });
                    column.Item().PaddingTop(6).Text(recall.Subject).FontSize(15).Bold();
                    column.Item().PaddingTop(6).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                });
                page.Content().PaddingTop(10).Column(content =>
                {
                    content.Spacing(12);
                    if (recall.Warnings.Count > 0)
                    {
                        content.Item().Background(Colors.Orange.Lighten4).Padding(8).Column(w =>
                        {
                            foreach (var warning in recall.Warnings)
                            {
                                w.Item().Text($"• {warning}");
                            }
                        });
                    }

                    content.Item().Text($"Clienti da avvisare ({recall.Customers.Count})").FontSize(12).Bold();
                    content.Item().Text(recall.Customers.Count == 0 ? "Nessuna consegna registrata." : string.Join(", ", recall.Customers));

                    content.Item().Text("Commesse coinvolte").FontSize(12).Bold();
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1.3f); c.RelativeColumn(2.5f); c.RelativeColumn(1.3f); c.RelativeColumn(1); c.RelativeColumn(1.2f); c.RelativeColumn(2); });
                        foreach (var header in new[] { "Commessa", "Prodotto", "Lotto prodotto", "Consumato", "Stato", "Matricole" })
                        {
                            table.Cell().BorderBottom(1).PaddingBottom(3).Text(header).Bold();
                        }

                        foreach (var order in recall.WorkOrders)
                        {
                            IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                            Cell().Text(order.Code);
                            Cell().Text($"{order.ProductCode} {order.ProductName}");
                            Cell().Text(order.ProductLotNumber ?? "-");
                            Cell().Text(order.ConsumedQuantity.ToString("0.###", italian));
                            Cell().Text(order.StatusLabel);
                            Cell().Text(order.AffectedSerials.Count == 0 ? "-" : string.Join(", ", order.AffectedSerials));
                        }
                    });

                    content.Item().Text("Consegne (DDT)").FontSize(12).Bold();
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2.5f); c.RelativeColumn(2.5f); c.RelativeColumn(1); c.RelativeColumn(1.2f); });
                        foreach (var header in new[] { "DDT", "Data", "Destinatario", "Articolo", "Quantità", "Lotto" })
                        {
                            table.Cell().BorderBottom(1).PaddingBottom(3).Text(header).Bold();
                        }

                        foreach (var shipment in recall.Shipments)
                        {
                            IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                            Cell().Text(shipment.DocumentCode);
                            Cell().Text(shipment.IssuedAt?.ToLocalTime().ToString("dd/MM/yyyy") ?? "-");
                            Cell().Text($"{shipment.RecipientName}{(shipment.RecipientAddress is null ? string.Empty : $"\n{shipment.RecipientAddress}")}");
                            Cell().Text(shipment.Description);
                            Cell().Text($"{shipment.Quantity.ToString("0.###", italian)} {shipment.Unit}");
                            Cell().Text(shipment.LotNumber ?? "-");
                        }
                    });

                    if (recall.Pallets.Count > 0)
                    {
                        content.Item().Text("Pallet (SSCC)").FontSize(12).Bold();
                        content.Item().Text(string.Join("   ", recall.Pallets.Select(p => $"{p.Sscc} ({p.LotNumber})")));
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

    /// <summary>Rapportino di intervento with the customer's signature, as left at the site.</summary>
    public static void ExportSiteReport(SiteReportDto report, CompanyProfileDto? company, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        byte[]? signature = null;
        const string pngHeader = "data:image/png;base64,";
        if (report.SignatureImage is { } image && image.StartsWith(pngHeader, StringComparison.Ordinal))
        {
            try
            {
                signature = Convert.FromBase64String(image[pngHeader.Length..]);
            }
            catch (FormatException)
            {
                signature = null;
            }
        }

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => Plain(style).FontSize(10));
                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(company?.CompanyName ?? string.Empty).FontSize(12).Bold();
                            if (!string.IsNullOrWhiteSpace(company?.Address))
                            {
                                left.Item().Text(company.Address).FontSize(9);
                            }
                        });
                        row.ConstantItem(230).AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text("RAPPORTINO DI INTERVENTO").FontSize(13).Bold();
                            right.Item().AlignRight().Text($"{report.Code} del {report.WorkDate:dd/MM/yyyy}").FontSize(10);
                        });
                    });
                    if (!report.IsSigned)
                    {
                        column.Item().PaddingTop(6).Border(1.5f).BorderColor(Colors.Red.Darken2).Padding(3).AlignCenter()
                            .Text("BOZZA - NON FIRMATO").Bold().FontColor(Colors.Red.Darken2);
                    }

                    column.Item().PaddingTop(8).LineHorizontal(0.75f).LineColor(Colors.Grey.Lighten1);
                });
                page.Content().PaddingTop(10).Column(content =>
                {
                    content.Spacing(10);
                    content.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(report.CustomerName ?? "-").Bold();
                            c.Item().Text(report.SiteAddress ?? string.Empty);
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Commessa").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Text($"{report.WorkOrderCode} · {report.ProductName}").Bold();
                        });
                    });
                    content.Item().Text("Lavori eseguiti").FontSize(8).FontColor(Colors.Grey.Darken1);
                    content.Item().Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).MinHeight(80).Text(report.Description);

                    content.Item().Text("Ore").FontSize(8).FontColor(Colors.Grey.Darken1);
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(2); c.RelativeColumn(1); });
                        foreach (var header in new[] { "Tecnico", "Reparto", "Ore" })
                        {
                            table.Cell().BorderBottom(1).PaddingBottom(3).Text(header).Bold();
                        }

                        foreach (var hours in report.Hours)
                        {
                            table.Cell().PaddingVertical(3).Text(hours.TechnicianName);
                            table.Cell().PaddingVertical(3).Text(hours.WorkCenterName ?? "-");
                            table.Cell().PaddingVertical(3).Text(hours.HoursText);
                        }

                        var total = report.Hours.Sum(h => h.Minutes);
                        table.Cell().BorderTop(0.75f).PaddingVertical(3).Text("Totale").Bold();
                        table.Cell().BorderTop(0.75f);
                        table.Cell().BorderTop(0.75f).PaddingVertical(3).Text($"{(int)(total / 60)}:{(int)(total % 60):00}").Bold();
                    });

                    content.Item().Text("Materiali installati").FontSize(8).FontColor(Colors.Grey.Darken1);
                    if (report.Materials.Count == 0)
                    {
                        content.Item().Text("Nessuno.");
                    }
                    else
                    {
                        content.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(1.3f); c.RelativeColumn(4); c.RelativeColumn(1); });
                            foreach (var header in new[] { "Codice", "Descrizione", "Quantità" })
                            {
                                table.Cell().BorderBottom(1).PaddingBottom(3).Text(header).Bold();
                            }

                            foreach (var material in report.Materials)
                            {
                                table.Cell().PaddingVertical(3).Text(material.MaterialCode ?? "-");
                                table.Cell().PaddingVertical(3).Text(material.Description);
                                table.Cell().PaddingVertical(3).Text($"{material.Quantity.ToString("0.###", italian)} {material.Unit}");
                            }
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(report.Notes))
                    {
                        content.Item().Text($"Note: {report.Notes}");
                    }

                    content.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Per il cliente").FontSize(8).FontColor(Colors.Grey.Darken1);
                            c.Item().Text(report.SignedByName ?? string.Empty).Bold();
                            c.Item().Text(report.SignedAt is { } signedAt ? $"Firmato il {signedAt.ToLocalTime():dd/MM/yyyy HH:mm}" : string.Empty).FontSize(8);
                        });
                        row.ConstantItem(220).Height(80).Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(4).Element(box =>
                        {
                            if (signature is not null)
                            {
                                box.Image(signature).FitArea();
                            }
                        });
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    /// <summary>Courtesy copy of an electronic invoice. The legally valid invoice is the XML delivered by
    /// the Exchange System: this copy says so, as the rules for courtesy copies require.</summary>
    public static void ExportInvoice(InvoiceDto invoice, CustomerFiscalDto customer, CompanyProfileDto? company, CompanyFiscalDto companyFiscal, string filePath)
    {
        var italian = System.Globalization.CultureInfo.GetCultureInfo("it-IT");
        string Money(decimal value) => value.ToString("N2", italian);
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(style => Plain(style).FontSize(9.5f));
                page.Header().Column(column =>
                {
                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Column(left =>
                        {
                            left.Item().Text(company?.CompanyName ?? string.Empty).FontSize(13).Bold();
                            left.Item().Text($"{companyFiscal.Street}, {companyFiscal.PostalCode} {companyFiscal.City} ({companyFiscal.Province})");
                            left.Item().Text($"P.IVA {company?.VatNumber}" + (companyFiscal.FiscalCode is null ? string.Empty : $" · C.F. {companyFiscal.FiscalCode}"));
                            if (companyFiscal.ReaNumber is not null)
                            {
                                left.Item().Text($"REA {companyFiscal.ReaOffice} {companyFiscal.ReaNumber}").FontSize(8.5f);
                            }
                        });
                        row.ConstantItem(230).AlignRight().Column(right =>
                        {
                            right.Item().AlignRight().Text(invoice.DocumentType == "TD24" ? "FATTURA DIFFERITA" : "FATTURA").FontSize(14).Bold();
                            right.Item().AlignRight().Text(invoice.Number is null ? "Bozza - non valida" : $"N. {invoice.Code} del {invoice.IssueDate:dd/MM/yyyy}").FontSize(11).Bold();
                        });
                    });
                    column.Item().PaddingTop(10).Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(c =>
                    {
                        c.Item().Text("Cliente").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                        c.Item().Text(invoice.CustomerName).FontSize(11).Bold();
                        c.Item().Text($"{customer.Street}, {customer.PostalCode} {customer.City} ({customer.Province}) {customer.Country}");
                        c.Item().Text(string.Join(" · ", new[]
                        {
                            customer.FiscalCode is null ? null : $"C.F. {customer.FiscalCode}",
                            customer.SdiCode is null ? null : $"Codice SDI {customer.SdiCode}",
                            customer.Pec is null ? null : $"PEC {customer.Pec}"
                        }.Where(v => v is not null)));
                    });
                    if (invoice.TransportDocuments.Count > 0)
                    {
                        column.Item().PaddingTop(6).Text("Riferimento DDT: " + string.Join(", ",
                            invoice.TransportDocuments.Select(d => $"{d.DocumentCode} del {d.IssuedAt?.ToLocalTime():dd/MM/yyyy}"))).FontSize(8.5f);
                    }
                });
                page.Content().PaddingTop(12).Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(4); c.ConstantColumn(50); c.ConstantColumn(40); c.ConstantColumn(62); c.ConstantColumn(44); c.ConstantColumn(44); c.ConstantColumn(70); });
                        foreach (var (text, right) in new[] { ("Descrizione", false), ("Q.tà", true), ("U.m.", false), ("Prezzo", true), ("Sconto", true), ("IVA", true), ("Importo", true) })
                        {
                            var cell = table.Cell().BorderBottom(1).PaddingBottom(3);
                            (right ? cell.AlignRight() : cell).Text(text).Bold().FontSize(8.5f);
                        }

                        foreach (var line in invoice.Lines)
                        {
                            IContainer Cell() => table.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                            Cell().Text((line.Code is null ? string.Empty : $"{line.Code} ") + line.Description);
                            Cell().AlignRight().Text(line.Quantity.ToString("0.###", italian));
                            Cell().Text(line.Unit);
                            Cell().AlignRight().Text(line.UnitPrice.ToString("#,##0.00##", italian));
                            Cell().AlignRight().Text(line.DiscountPercent > 0 ? $"{line.DiscountPercent:0.##}%" : "-");
                            Cell().AlignRight().Text(line.VatRate == 0 ? line.VatNature ?? "0%" : $"{line.VatRate:0}%");
                            Cell().AlignRight().Text(Money(line.LineTotal));
                        }
                    });

                    content.Item().PaddingTop(10).AlignRight().Width(260).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        foreach (var text in new[] { "Aliquota", "Imponibile", "Imposta" })
                        {
                            table.Cell().BorderBottom(1).PaddingBottom(2).AlignRight().Text(text).Bold().FontSize(8.5f);
                        }

                        foreach (var summary in invoice.VatSummary)
                        {
                            table.Cell().AlignRight().Text(summary.Rate == 0 ? summary.Nature ?? "0%" : $"{summary.Rate:0}%");
                            table.Cell().AlignRight().Text(Money(summary.Taxable));
                            table.Cell().AlignRight().Text(Money(summary.Tax));
                        }
                    });
                    content.Item().PaddingTop(6).AlignRight().Text($"Totale documento {Money(invoice.Total)} €").FontSize(13).Bold();
                    var payment = invoice.PaymentMethod switch { "MP05" => "Bonifico", "MP12" => "RIBA", "MP01" => "Contanti", "MP02" => "Assegno", "MP08" => "Carta", "MP19" => "SEPA Direct Debit", _ => invoice.PaymentMethod };
                    content.Item().PaddingTop(10).Text($"Pagamento: {payment}" + (invoice.PaymentDueDate is { } due ? $" entro il {due:dd/MM/yyyy}" : string.Empty)
                        + (invoice.PaymentMethod == "MP05" && companyFiscal.Iban is not null ? $" · IBAN {companyFiscal.Iban}" : string.Empty));
                    if (!string.IsNullOrWhiteSpace(invoice.Notes))
                    {
                        content.Item().PaddingTop(4).Text(invoice.Notes);
                    }

                    if (invoice.VatSummary.Any(s => s.Nature is not null && s.Nature.StartsWith("N6")))
                    {
                        content.Item().PaddingTop(4).Text("Operazione soggetta a inversione contabile (reverse charge), art. 17 DPR 633/72: l'IVA è a carico del committente.").Bold();
                    }
                });
                page.Footer().Column(footer =>
                {
                    footer.Item().AlignCenter().Text("Copia di cortesia. La fattura valida ai fini fiscali è il file elettronico trasmesso tramite il Sistema di Interscambio (SdI).").FontSize(7.5f).FontColor(Colors.Grey.Darken1);
                    footer.Item().AlignCenter().Text(text =>
                    {
                        text.CurrentPageNumber();
                        text.Span(" / ");
                        text.TotalPages();
                    });
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
