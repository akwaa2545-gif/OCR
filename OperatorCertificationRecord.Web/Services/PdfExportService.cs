using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Data;

namespace OperatorCertificationRecord.Web.Services;

public class PdfExportService
{
    public byte[] GeneratePdfFromDataTable(DataTable dt, string title = "Report")
    {
        using var ms = new System.IO.MemoryStream();
        // If no data, render a simple page saying "No data available"
        if (dt == null || dt.Columns.Count == 0 || dt.Rows.Count == 0)
        {
            var emptyDoc = Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(20);
                    page.DefaultTextStyle(x => x.FontSize(12));

                    page.Header().Text(title).FontSize(16).Bold();

                    page.Content().AlignMiddle().AlignCenter().Text("No data available").FontSize(12);

                    page.Footer().AlignCenter().Text($"Generated: {DateTime.UtcNow:u}");
                });
            });

            emptyDoc.GeneratePdf(ms);
            return ms.ToArray();
        }

        var doc = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(20);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Text(title).FontSize(16).Bold();

                page.Content().Column(col =>
                {
                    col.Item().Element(c => RenderTable(c, dt));
                });

                page.Footer().AlignCenter().Text($"Generated: {DateTime.UtcNow:u}");
            });
        });

        doc.GeneratePdf(ms);
        return ms.ToArray();
    }

    private void RenderTable(IContainer container, DataTable dt)
    {
        container.PaddingTop(5).Table(table =>
        {
            int colsCount = dt.Columns.Count;

            // reduce font size if many columns to avoid tiny columns
            var cellFontSize = colsCount > 10 ? 8 : 10;

            table.ColumnsDefinition(columns =>
            {
                for (int i = 0; i < colsCount; i++) columns.RelativeColumn();
            });

            // header
            table.Header(header =>
            {
                foreach (DataColumn c in dt.Columns)
                {
                    header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text(c.ColumnName).SemiBold().FontSize(cellFontSize);
                }
            });

            // rows
            foreach (DataRow r in dt.Rows)
            {
                foreach (DataColumn c in dt.Columns)
                {
                    object? value = r[c];
                    string text;

                    if (value is DateTime dtVal)
                    {
                        text = dtVal.ToString("yyyy-MM-dd");
                    }
                    else
                    {
                        text = value?.ToString() ?? string.Empty;
                    }

                    table.Cell().Padding(4).Text(text).FontSize(cellFontSize);
                }
            }
        });
    }
}
