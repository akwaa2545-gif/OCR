using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Pages.Download;

public class SectionModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly Services.SectionService _sectionService;
    private readonly Services.PdfExportService _pdfExportService;
    public SectionModel(IConfiguration configuration, Services.SectionService sectionService, Services.PdfExportService pdfExportService)
    {
        _configuration = configuration;
        _sectionService = sectionService;
        _pdfExportService = pdfExportService;
    }

    public List<Models.Section>? Sections { get; set; }

    public async Task<IActionResult> OnGetAsync(string section, string? department, DateTime? start, DateTime? end, string format = "xlsx")
    {
        // If section parameter provided, download the Excel file
        if (!string.IsNullOrWhiteSpace(section) || !string.IsNullOrWhiteSpace(department))
        {
            return await GenerateExcelAsync(section, department, start, end, format);
        }
        
        // Otherwise just show the page
        Sections = await _sectionService.GetAllSectionsAsync();
        return Page();
    }

        private async Task<IActionResult> GenerateExcelAsync(string section, string? department, DateTime? start, DateTime? end, string format)
    {
            if (string.IsNullOrWhiteSpace(section) && string.IsNullOrWhiteSpace(department))
                return BadRequest("Missing section or department");

        var conn = new SqlConnection(_configuration.GetConnectionString("DefaultConnection") ?? "");
        await conn.OpenAsync();
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("SELECT q.EmpCode,q.JoinDate,q.HEng,q.PersonFNameEng,q.PersonLNameEng,q.HThai,q.PersonFNameThai,q.PersonLNameThai,q.DeptName,q.SectName,q.WorkshopName,q.JobGrade,q.Shift,q.ProcessName,q.OperatorTraining,q.TheoryTraining,q.OJTTraining,q.FullScore,q.ActualScore,q.TestResult,q.JudgmentTheory,q.KnowledgeScore,q.KnowledgeLevel,q.SkillScore,q.SkillLevel,q.JudgmentPractice,q.CertifiedDate,q.ExpiryDate,");
            sb.AppendLine("       CASE WHEN v.PersonFnameEng IS NOT NULL AND v.PersonFnameEng <> '' THEN LTRIM(RTRIM(CONCAT(v.PersonFnameEng, ' ', ISNULL(v.PersonLnameEng, '')))) ELSE q.Verifier END AS Verifier,");
            sb.AppendLine("       q.VerifierDate,q.Remark");
            sb.AppendLine("FROM ViewEmpQualified_All q");
            sb.AppendLine("LEFT JOIN tblEmployee v ON v.EmpCode = q.Verifier");
            if (!string.IsNullOrWhiteSpace(department))
            {
                sb.AppendLine("WHERE q.DeptName = @DeptName");
            }
            else
            {
                sb.AppendLine("WHERE q.SectName = @SectName");
            }
            sb.AppendLine("  AND (q.DisQualifiedBy = '' OR q.DisQualifiedBy IS NULL)");
            sb.AppendLine("  AND q.ResignDate IS NULL");
            sb.AppendLine("  AND (q.Remark IS NULL OR (q.Remark NOT LIKE '%PROMOTED%' AND q.Remark NOT LIKE '%RESIGNED%'))");
            sb.AppendLine("  AND q.ProcessName IS NOT NULL");

            if (start.HasValue && end.HasValue)
            {
                sb.AppendLine(" AND q.CertifiedDate BETWEEN @start AND @end");
            }

            using (var cmd = new SqlCommand(sb.ToString(), conn))
            {
                if (!string.IsNullOrWhiteSpace(department))
                {
                    cmd.Parameters.AddWithValue("@DeptName", department);
                }
                else
                {
                    cmd.Parameters.AddWithValue("@SectName", section);
                }
                if (start.HasValue && end.HasValue)
                {
                    var s = start.Value.Date;
                    var e = end.Value.Date.AddDays(1).AddTicks(-1);
                    cmd.Parameters.Add("@start", System.Data.SqlDbType.DateTime).Value = s;
                    cmd.Parameters.Add("@end", System.Data.SqlDbType.DateTime).Value = e;
                }

                using (var rdr = await cmd.ExecuteReaderAsync())
                {
                    var dt = new System.Data.DataTable();
                    dt.Load(rdr);

                    if ((format?.ToLower() ?? "xlsx") == "pdf")
                    {
                        var pdf = _pdfExportService.GeneratePdfFromDataTable(dt, $"Operator Training - {section}");
                        var pdfName = $"Operator_training_by_{SanitizeFileName(section)}_{DateTime.Now:yyyyMMdd}.pdf";
                        return File(pdf, "application/pdf", pdfName);
                    }

                    using (var workbook = new ClosedXML.Excel.XLWorkbook())
                    {
                        var ws = workbook.Worksheets.Add("Section");

                        // Write headers
                        for (int c = 0; c < dt.Columns.Count; c++) ws.Cell(1, c + 1).Value = dt.Columns[c].ColumnName;

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
                                var v = dt.Rows[r][c];
                                var cell = ws.Cell(r + 2, c + 1);
                                if (v is DateTime dtVal)
                                {
                                    cell.Value = dtVal;
                                    cell.Style.DateFormat.Format = "dd-mmm-yyyy";
                                }
                                else
                                {
                                    cell.SetValue(Convert.ToString(v));
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

                        using (var ms = new System.IO.MemoryStream())
                        {
                            workbook.SaveAs(ms);
                            ms.Position = 0;
                            var fileName = $"Operator training by {section} {DateTime.Now:yyyyMMdd}.xlsx";
                            return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
                        }
                    }
                }
            }
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
