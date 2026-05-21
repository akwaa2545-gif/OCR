using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using System.IO;

namespace OperatorCertificationRecord.Web.Pages.Download;

public class SkillInventoryModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly Services.PdfExportService _pdfExportService;

    public SkillInventoryModel(IConfiguration configuration, Services.PdfExportService pdfExportService)
    {
        _configuration = configuration;
        _pdfExportService = pdfExportService;
    }

    public async Task<IActionResult> OnGetAsync(string? section, string? department, DateTime? start, DateTime? end, string format = "xlsx")
    {
        // Generate and return the Excel file directly, optionally filtered by section/department and date
        var connStr = _configuration.GetConnectionString("DefaultConnection") ?? "";
        if (string.IsNullOrWhiteSpace(connStr)) return StatusCode(500, "Database connection string not configured.");

        await using var conn = new SqlConnection(connStr);
        await conn.OpenAsync();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("SELECT q.EmpCode,q.JoinDate,q.HEng,q.PersonFNameEng,q.PersonLNameEng,q.HThai,q.PersonFNameThai,q.PersonLNameThai,q.DeptName,q.SectName,q.WorkshopName,q.JobGrade,q.Shift,q.ProcessName,q.OperatorTraining,q.TheoryTraining,q.OJTTraining,q.FullScore,q.ActualScore,q.TestResult,q.JudgmentTheory,q.KnowledgeScore,q.KnowledgeLevel,q.SkillScore,q.SkillLevel,q.JudgmentPractice,q.CertifiedDate,q.ExpiryDate,");
        sb.AppendLine("       CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN LTRIM(RTRIM(CONCAT(v.PersonFnameEng, ' ', ISNULL(v.PersonLnameEng, '')))) ELSE q.Verifier END AS Verifier,");
        sb.AppendLine("       q.VerifierDate,q.Remark");
        sb.AppendLine("FROM ViewEmpQualified_All q");
        sb.AppendLine("LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier");
        sb.AppendLine("WHERE (q.DisQualifiedBy = '' OR q.DisQualifiedBy IS NULL)");
        sb.AppendLine("  AND q.ResignDate IS NULL");
        sb.AppendLine("  AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))");
        sb.AppendLine("  AND q.ProcessName IS NOT NULL");
        if (!string.IsNullOrWhiteSpace(department)) sb.AppendLine("  AND q.DeptName = @DeptName");
        if (!string.IsNullOrWhiteSpace(section)) sb.AppendLine("  AND q.SectName = @SectName");
        if (start.HasValue && end.HasValue) sb.AppendLine("  AND q.CertifiedDate BETWEEN @start AND @end");

        var dt = new System.Data.DataTable();
        try
        {
            using var cmd = new SqlCommand(sb.ToString(), conn);
            if (!string.IsNullOrWhiteSpace(department)) cmd.Parameters.AddWithValue("@DeptName", department);
            if (!string.IsNullOrWhiteSpace(section)) cmd.Parameters.AddWithValue("@SectName", section);
            if (start.HasValue && end.HasValue)
            {
                var s = start.Value.Date;
                var e = end.Value.Date.AddDays(1).AddTicks(-1);
                cmd.Parameters.Add("@start", System.Data.SqlDbType.DateTime).Value = s;
                cmd.Parameters.Add("@end", System.Data.SqlDbType.DateTime).Value = e;
            }
            using var da = new System.Data.SqlClient.SqlDataAdapter(cmd);
            da.Fill(dt);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error querying database: " + ex.Message);
        }

        // include section in filename when provided
        var namePart = !string.IsNullOrWhiteSpace(section) ? section : "SkillInventory";
        var safeName = SanitizeFileName(namePart);
        var fileBase = $"Operator_training_{safeName}_{DateTime.Now:yyyyMMdd}";

        try
        {
            if ((format?.ToLower() ?? "xlsx") == "pdf")
            {
                var pdf = _pdfExportService.GeneratePdfFromDataTable(dt, "Skill Inventory");
                return File(pdf, "application/pdf", fileBase + ".pdf");
            }
            if (format?.ToLower() == "csv")
            {
                var sbCsv = new System.Text.StringBuilder();
                var cols = dt.Columns.Cast<System.Data.DataColumn>().Select(c => c.ColumnName);
                sbCsv.AppendLine(string.Join(",", cols.Select(EscapeCsv)));
                foreach (System.Data.DataRow r in dt.Rows)
                {
                    var items = dt.Columns.Cast<System.Data.DataColumn>().Select(c => EscapeCsv(Convert.ToString(r[c])));
                    sbCsv.AppendLine(string.Join(",", items));
                }
                var bytes = System.Text.Encoding.UTF8.GetBytes(sbCsv.ToString());
                return File(bytes, "text/csv", fileBase + ".csv");
            }

            using var wb = new ClosedXML.Excel.XLWorkbook();
            var ws = wb.Worksheets.Add("SkillInventory");

            // Write headers
            for (int i = 0; i < dt.Columns.Count; i++) ws.Cell(1, i + 1).Value = dt.Columns[i].ColumnName;

            // Style header row – dark blue, white bold text
            var hdr = ws.Row(1);
            hdr.Style.Font.Bold = true;
            hdr.Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
            hdr.Style.Font.FontSize = 11;
            hdr.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#1A3A52");
            hdr.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;
            hdr.Style.Alignment.Vertical = ClosedXML.Excel.XLAlignmentVerticalValues.Center;
            hdr.Height = 20;

            // Data rows with alternating fill
            for (int r = 0; r < dt.Rows.Count; r++)
            {
                for (int c = 0; c < dt.Columns.Count; c++)
                {
                    var val = dt.Rows[r][c];
                    var colType = dt.Columns[c].DataType;
                    if (val != DBNull.Value && val != null && (colType == typeof(DateTime) || colType == typeof(DateTime?)))
                    {
                        var cell = ws.Cell(r + 2, c + 1);
                        cell.Value = (DateTime)val;
                        cell.Style.DateFormat.Format = "dd-mmm-yyyy";
                    }
                    else
                    {
                        ws.Cell(r + 2, c + 1).SetValue(Convert.ToString(val));
                    }
                }
                ws.Row(r + 2).Style.Fill.BackgroundColor = r % 2 == 0
                    ? ClosedXML.Excel.XLColor.White
                    : ClosedXML.Excel.XLColor.FromHtml("#EEF3F8");
            }

            // Borders, AutoFilter, freeze header
            if (dt.Rows.Count > 0)
            {
                var range = ws.Range(1, 1, dt.Rows.Count + 1, dt.Columns.Count);
                range.Style.Border.OutsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                range.Style.Border.OutsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#90A4AE");
                range.Style.Border.InsideBorder = ClosedXML.Excel.XLBorderStyleValues.Thin;
                range.Style.Border.InsideBorderColor = ClosedXML.Excel.XLColor.FromHtml("#CFD8DC");
                range.SetAutoFilter();
            }
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();

            await using var ms = new System.IO.MemoryStream();
            wb.SaveAs(ms);
            ms.Seek(0, System.IO.SeekOrigin.Begin);
            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileBase + ".xlsx");
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Error generating file: " + ex.Message);
        }
    }

    private static string EscapeCsv(string? value)
    {
        if (value == null) return string.Empty;
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }
        return value;
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "export";
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(name);
        for (int i = 0; i < sb.Length; i++)
        {
            if (Array.IndexOf(invalid, sb[i]) >= 0) sb[i] = '_';
            else if (char.IsWhiteSpace(sb[i])) sb[i] = '_';
        }
        return sb.ToString().Trim('_');
    }
}
