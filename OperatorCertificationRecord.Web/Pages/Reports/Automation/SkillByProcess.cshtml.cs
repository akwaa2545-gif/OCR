using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.Reports.Automation;

[AdminOnly]
public class SkillByProcessModel : PageModel
{
    private readonly ReportAutomationService _svc;
    private readonly ILogger<SkillByProcessModel> _log;

    public List<string> Workshops { get; set; } = new();
    public List<SkillByProcessRow> Rows { get; set; } = new();

    // Two-level grouping: Section → Workshop → rows
    public Dictionary<string, Dictionary<string, List<SkillByProcessRow>>> GroupedBySection { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? WorkshopFilter { get; set; }

    public SkillByProcessModel(ReportAutomationService svc, ILogger<SkillByProcessModel> log)
    {
        _svc = svc;
        _log = log;
    }

    public async Task OnGetAsync()
    {
        try
        {
            Workshops = await _svc.GetWorkshopNamesAsync();
            Rows = await _svc.GetSkillByProcessAsync(WorkshopFilter);
            GroupedBySection = BuildGrouped(Rows);
        }
        catch (Exception ex) { _log.LogError(ex, "SkillByProcess load error"); }
    }

    // Custom section display order: BOL → MOL → SC Mfg → Inspection → Evaluation/CQE_Ta → NNECL → everything else
    private static readonly List<string> SectionSortPrefixes = new()
    {
        "BOL", "MOL", "SC", "Inspection", "Evaluat", "CQE", "NNECL"
    };

    private static int SectionSortIndex(string sectionName)
    {
        for (int i = 0; i < SectionSortPrefixes.Count; i++)
            if (sectionName.StartsWith(SectionSortPrefixes[i], StringComparison.OrdinalIgnoreCase))
                return i;
        return SectionSortPrefixes.Count;
    }

    private static Dictionary<string, Dictionary<string, List<SkillByProcessRow>>> BuildGrouped(List<SkillByProcessRow> rows)
    {
        return rows
            .GroupBy(r => string.IsNullOrEmpty(r.SectionName) ? "Unknown" : r.SectionName)
            .OrderBy(sg => SectionSortIndex(sg.Key))
            .ThenBy(sg => sg.Key)
            .ToDictionary(
                sg => sg.Key,
                sg => sg.GroupBy(r => r.WorkshopName)
                         .OrderBy(wg => wg.Key)
                         .ToDictionary(wg => wg.Key, wg => wg.ToList())
            );
    }

    public async Task<IActionResult> OnPostDownloadAsync()
    {
        var filter = Request.Form["WorkshopFilter"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(filter)) filter = null;
        Rows = await _svc.GetSkillByProcessAsync(filter);
        GroupedBySection = BuildGrouped(Rows);

        using var wb = new XLWorkbook();

        // ── Summary sheet (always created first so workbook is never empty) ──
        var summary = wb.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Skill by Process — All Areas";
        summary.Cell(1, 1).Style.Font.Bold = true;
        summary.Cell(1, 1).Style.Font.FontSize = 13;
        summary.Range(1, 1, 1, 6).Merge();

        if (!Rows.Any())
        {
            summary.Cell(2, 1).Value = "No data found for the selected filter.";
            summary.Cell(2, 1).Style.Font.Italic = true;
        }
        else
        {
            int sr = 2;

            // ── Section Summary Overview table ───────────────────────────────
            summary.Cell(sr, 1).Value = "Section Summary";
            summary.Cell(sr, 1).Style.Font.Bold = true;
            summary.Cell(sr, 1).Style.Font.FontSize = 11;
            summary.Range(sr, 1, sr, 4).Merge();
            summary.Row(sr).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e293b");
            summary.Row(sr).Style.Font.FontColor = XLColor.White;
            sr++;

            var sectHdrs = new[] { "Section", "Workshops", "Processes", "Total Qualified" };
            for (int c = 0; c < sectHdrs.Length; c++)
            {
                var h = summary.Cell(sr, c + 1);
                h.Value = sectHdrs[c];
                h.Style.Font.Bold = true;
                h.Style.Fill.BackgroundColor = XLColor.FromHtml("#334155");
                h.Style.Font.FontColor = XLColor.White;
                h.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                h.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            sr++;

            int grandWs = 0, grandProc = 0, grandQual = 0;
            foreach (var (sn, wss) in GroupedBySection)
            {
                int sProc = wss.Values.Sum(r => r.Count);
                int sQual = wss.Values.Sum(r => r.Sum(x => x.CountQualified));
                grandWs   += wss.Count;
                grandProc += sProc;
                grandQual += sQual;

                summary.Cell(sr, 1).Value = sn;
                summary.Cell(sr, 2).Value = wss.Count;
                summary.Cell(sr, 3).Value = sProc;
                summary.Cell(sr, 4).Value = sQual;
                for (int c = 1; c <= 4; c++)
                {
                    summary.Cell(sr, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    if (c > 1) summary.Cell(sr, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                sr++;
            }

            // Grand total row
            summary.Cell(sr, 1).Value = "Grand Total";
            summary.Cell(sr, 1).Style.Font.Bold = true;
            summary.Cell(sr, 2).Value = grandWs;
            summary.Cell(sr, 3).Value = grandProc;
            summary.Cell(sr, 4).Value = grandQual;
            for (int c = 1; c <= 4; c++)
            {
                summary.Cell(sr, c).Style.Font.Bold = true;
                summary.Cell(sr, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                summary.Cell(sr, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (c > 1) summary.Cell(sr, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            sr += 2; // blank row separator

            // ── Detail table (all rows, grouped by section) ──────────────────
            var hdrs = new[] { "Section", "Workshop", "Process Name", "Qualified Count", "Need", "Balance" };
            for (int c = 0; c < hdrs.Length; c++)
            {
                var cell = summary.Cell(sr, c + 1);
                cell.Value = hdrs[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f2937");
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            sr++;

            string lastSect = "";
            foreach (var row in Rows)
            {
                if (row.SectionName != lastSect)
                {
                    // Section group header row
                    summary.Cell(sr, 1).Value = row.SectionName;
                    summary.Range(sr, 1, sr, 6).Merge();
                    summary.Cell(sr, 1).Style.Font.Bold = true;
                    summary.Cell(sr, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#374151");
                    summary.Cell(sr, 1).Style.Font.FontColor = XLColor.White;
                    lastSect = row.SectionName;
                    sr++;
                }
                summary.Cell(sr, 1).Value = row.SectionName;
                summary.Cell(sr, 2).Value = row.WorkshopName;
                summary.Cell(sr, 3).Value = row.ProcessName;
                summary.Cell(sr, 4).Value = row.CountQualified;
                summary.Cell(sr, 5).Value = row.Need;
                summary.Cell(sr, 6).Value = row.Balance;
                for (int c = 1; c <= 6; c++) summary.Cell(sr, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                sr++;
            }

            summary.Columns().AdjustToContents();
        }

        // ── Per-workshop sheets ───────────────────────────────────────────────
        int wsIdx = 1;
        foreach (var (sectName, workshops) in GroupedBySection)
        {
            foreach (var (areaName, areaRows) in workshops)
            {
                var sheet = wb.Worksheets.Add(SafeSheetName(areaName, wsIdx++));

                sheet.Cell(1, 1).Value = $"Skill by Process — {sectName} / {areaName}";
                sheet.Cell(1, 1).Style.Font.Bold = true;
                sheet.Cell(1, 1).Style.Font.FontSize = 13;
                sheet.Range(1, 1, 1, 5).Merge();

                sheet.Cell(2, 1).Value = $"Section: {sectName}";
                sheet.Cell(2, 1).Style.Font.Italic = true;
                sheet.Cell(2, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#374151");
                sheet.Cell(2, 1).Style.Font.FontColor = XLColor.White;
                sheet.Range(2, 1, 2, 5).Merge();

                var hdrs = new[] { "No.", "Process Name", "Qualified Count", "Need", "Balance" };
                for (int c = 0; c < hdrs.Length; c++)
                {
                    var cell = sheet.Cell(3, c + 1);
                    cell.Value = hdrs[c];
                    cell.Style.Font.Bold = true;
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f2937");
                    cell.Style.Font.FontColor = XLColor.White;
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                int r = 4, no = 1, total = 0;
                foreach (var row in areaRows)
                {
                    sheet.Cell(r, 1).Value = no++;
                    sheet.Cell(r, 2).Value = row.ProcessName;
                    sheet.Cell(r, 3).Value = row.CountQualified;
                    sheet.Cell(r, 4).Value = row.Need;
                    sheet.Cell(r, 5).Value = row.Balance;
                    total += row.CountQualified;

                    if (r % 2 == 0)
                        for (int c = 1; c <= 5; c++)
                            sheet.Cell(r, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");

                    for (int c = 1; c <= 5; c++)
                        sheet.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    r++;
                }

                sheet.Cell(r, 1).Value = "Total";
                sheet.Cell(r, 1).Style.Font.Bold = true;
                sheet.Range(r, 1, r, 2).Merge();
                sheet.Cell(r, 3).Value = total;
                sheet.Cell(r, 3).Style.Font.Bold = true;
                sheet.Cell(r, 4).Value = 0;
                sheet.Cell(r, 5).Value = total;
                for (int c = 1; c <= 5; c++)
                {
                    sheet.Cell(r, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                    sheet.Cell(r, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }

                sheet.Columns().AdjustToContents();
            }
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"SkillByProcess_{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

    private static string SafeSheetName(string name, int idx)
    {
        var safe = string.Concat(name.Where(c => !"[\\]/?*:'".Contains(c)));
        if (safe.Length > 28) safe = safe[..28];
        return string.IsNullOrWhiteSpace(safe) ? $"Area{idx}" : safe;
    }


}


