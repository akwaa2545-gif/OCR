using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.Reports.Automation;

[AdminOnly]
public class ILUOSkillLevelModel : PageModel
{
    private readonly ReportAutomationService _svc;
    private readonly ILogger<ILUOSkillLevelModel> _log;

    public List<ILUOWorkshopRow> Rows { get; set; } = new();
    public Dictionary<string, List<ILUOWorkshopRow>> Grouped { get; set; } = new();
    public List<ILUOMonthRow> MonthRows { get; set; } = new();

    public ILUOSkillLevelModel(ReportAutomationService svc, ILogger<ILUOSkillLevelModel> log)
    {
        _svc = svc;
        _log = log;
    }

    public async Task OnGetAsync()
    {
        try
        {
            var rowsTask   = _svc.GetILUOSkillLevelAsync();
            var monthsTask = _svc.GetILUOSkillByMonthAsync();
            await Task.WhenAll(rowsTask, monthsTask);
            Rows      = rowsTask.Result;
            MonthRows = monthsTask.Result;
            Grouped = Rows
                .GroupBy(r => string.IsNullOrEmpty(r.SectionName) ? "Unknown" : r.SectionName)
                .OrderBy(g => g.Key)
                .ToDictionary(g => g.Key, g => g.ToList());
        }
        catch (Exception ex) { _log.LogError(ex, "ILUOSkillLevel load error"); }
    }

    public async Task<IActionResult> OnPostDownloadAsync()
    {
        var rowsTask   = _svc.GetILUOSkillLevelAsync();
        var monthsTask = _svc.GetILUOSkillByMonthAsync();
        await Task.WhenAll(rowsTask, monthsTask);
        Rows      = rowsTask.Result;
        MonthRows = monthsTask.Result;
        Grouped = Rows
            .GroupBy(r => string.IsNullOrEmpty(r.SectionName) ? "Unknown" : r.SectionName)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ILUO Skill Level");

        // ── Header row 1: title
        ws.Cell(1, 1).Value = "ILUO Skill Level Report";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        ws.Range(1, 1, 1, 11).Merge();

        // ── Header row 2: columns
        var headers = new[] { "Section", "Workshop / Area", "No. Operator", "Process Skill",
            "Level - X", "Level - I", "Level - L", "Level - U", "Level - O",
            "Level U+O", "Ratio U+O (%)" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(2, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1f2937");
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // ── Data rows grouped by section
        int row = 3;
        int totalOp = 0, totalProc = 0, totalX = 0, totalI = 0, totalL = 0, totalU = 0, totalO = 0;
        foreach (var (sectName, sectRows) in Grouped)
        {
            // Section header row
            var sectCell = ws.Cell(row, 1);
            sectCell.Value = sectName;
            sectCell.Style.Font.Bold = true;
            sectCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#374151");
            sectCell.Style.Font.FontColor = XLColor.White;
            ws.Range(row, 1, row, 11).Merge();
            ws.Range(row, 1, row, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            row++;

            int sOp = 0, sProc = 0, sX = 0, sI = 0, sL = 0, sU = 0, sO = 0;
            foreach (var r in sectRows)
            {
                ws.Cell(row, 1).Value = r.SectionName;
                ws.Cell(row, 2).Value = r.WorkshopName;
                ws.Cell(row, 3).Value = r.NoOperator;
                ws.Cell(row, 4).Value = r.ProcessSkill;
                ws.Cell(row, 5).Value = r.LevelX;
                ws.Cell(row, 6).Value = r.LevelI;
                ws.Cell(row, 7).Value = r.LevelL;
                ws.Cell(row, 8).Value = r.LevelU;
                ws.Cell(row, 9).Value = r.LevelO;
                ws.Cell(row, 10).Value = r.LevelUO;
                ws.Cell(row, 11).Value = r.RatioUO;

                ws.Cell(row, 10).Style.Fill.BackgroundColor = XLColor.FromHtml("#fef3c7");
                ws.Cell(row, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#fef3c7");

                for (int c = 1; c <= 11; c++)
                    ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                sOp += r.NoOperator; sProc += r.ProcessSkill;
                sX += r.LevelX; sI += r.LevelI; sL += r.LevelL;
                sU += r.LevelU; sO += r.LevelO;
                row++;
            }

            // Section subtotal row
            ws.Cell(row, 1).Value = "";
            ws.Cell(row, 2).Value = $"Subtotal — {sectName}";
            ws.Cell(row, 3).Value = sOp;
            ws.Cell(row, 4).Value = sProc;
            ws.Cell(row, 5).Value = sX;
            ws.Cell(row, 6).Value = sI;
            ws.Cell(row, 7).Value = sL;
            ws.Cell(row, 8).Value = sU;
            ws.Cell(row, 9).Value = sO;
            int sUO = sU + sO;
            ws.Cell(row, 10).Value = sUO;
            ws.Cell(row, 11).Value = sProc > 0 ? Math.Round((double)sUO / sProc * 100, 2) : 0;
            for (int c = 1; c <= 11; c++)
            {
                ws.Cell(row, c).Style.Font.Bold = true;
                ws.Cell(row, c).Style.Font.Italic = true;
                ws.Cell(row, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#f3f4f6");
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            totalOp += sOp; totalProc += sProc;
            totalX += sX; totalI += sI; totalL += sL;
            totalU += sU; totalO += sO;
            row++;
        }

        // ── Grand Total row
        var totalRow = row;
        ws.Cell(totalRow, 1).Value = "";
        ws.Cell(totalRow, 2).Value = "Grand Total";
        ws.Cell(totalRow, 3).Value = totalOp;
        ws.Cell(totalRow, 4).Value = totalProc;
        ws.Cell(totalRow, 5).Value = totalX;
        ws.Cell(totalRow, 6).Value = totalI;
        ws.Cell(totalRow, 7).Value = totalL;
        ws.Cell(totalRow, 8).Value = totalU;
        ws.Cell(totalRow, 9).Value = totalO;
        int totalUO = totalU + totalO;
        ws.Cell(totalRow, 10).Value = totalUO;
        ws.Cell(totalRow, 11).Value = totalProc > 0 ? Math.Round((double)totalUO / totalProc * 100, 2) : 0;
        for (int c = 1; c <= 11; c++)
        {
            ws.Cell(totalRow, c).Style.Font.Bold = true;
            ws.Cell(totalRow, c).Style.Fill.BackgroundColor = XLColor.FromHtml("#1f2937");
            ws.Cell(totalRow, c).Style.Font.FontColor = XLColor.White;
            ws.Cell(totalRow, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        ws.Columns().AdjustToContents();
        ws.Column(10).Width = 14;

        // ════════════════════════════════════════════════════════════════════
        // SHEET 2 — Skill Level Summary by Month
        // ════════════════════════════════════════════════════════════════════
        if (MonthRows.Any())
        {
            var mws = wb.Worksheets.Add("By Month");
            var ordered = MonthRows.OrderBy(m => m.MonthSort).ToList();
            int nowSort = DateTime.Now.Year * 100 + DateTime.Now.Month;
            int totalMonthCols = 1 + ordered.Count; // Level col + month cols

            // ── Title ──
            mws.Row(1).Height = 26;
            mws.Cell(1, 1).Value = $"Skill Level Summary by Month  ·  As of {DateTime.Now:dd MMMM yyyy}";
            mws.Cell(1, 1).Style.Font.Bold            = true;
            mws.Cell(1, 1).Style.Font.FontSize        = 13;
            mws.Cell(1, 1).Style.Font.FontColor       = XLColor.White;
            mws.Cell(1, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            mws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mws.Cell(1, 1).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            mws.Range(1, 1, 1, totalMonthCols).Merge();

            // ── Header row: "Level" + month labels ──
            mws.Row(2).Height = 20;
            var lvlHdr = mws.Cell(2, 1);
            lvlHdr.Value = "Level";
            lvlHdr.Style.Font.Bold            = true;
            lvlHdr.Style.Font.FontColor       = XLColor.White;
            lvlHdr.Style.Fill.BackgroundColor = XLColor.FromHtml("#2d5282");
            lvlHdr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            lvlHdr.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            lvlHdr.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            for (int mi = 0; mi < ordered.Count; mi++)
            {
                var hc = mws.Cell(2, 2 + mi);
                hc.Value = ordered[mi].MonthLabel;
                hc.Style.Font.Bold            = true;
                hc.Style.Font.FontColor       = XLColor.White;
                hc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                hc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                hc.Style.Fill.BackgroundColor = ordered[mi].MonthSort == nowSort
                    ? XLColor.FromHtml("#b45309")   // current month: amber
                    : ordered[mi].MonthSort > nowSort
                        ? XLColor.FromHtml("#475569") // future: slate
                        : XLColor.FromHtml("#334155"); // past: dark slate
            }

            // ── Level rows ──
            var levels = new[]
            {
                ("X", "#a3e635", "#365314", (Func<ILUOMonthRow,int>)(m => m.LevelX)),
                ("I", "#22c55e", "#14532d", m => m.LevelI),
                ("L", "#ec4899", "#831843", m => m.LevelL),
                ("U", "#fbbf24", "#78350f", m => m.LevelU),
                ("O", "#3b82f6", "#1e3a8a", m => m.LevelO),
            };

            int dataRow = 3;
            foreach (var (lvl, bgHex, fgHex, getter) in levels)
            {
                mws.Row(dataRow).Height = 17;
                var lc = mws.Cell(dataRow, 1);
                lc.Value = $"Level - {lvl}";
                lc.Style.Font.Bold            = true;
                lc.Style.Font.FontColor       = XLColor.FromHtml(fgHex);
                lc.Style.Fill.BackgroundColor = XLColor.FromHtml(bgHex + "40"); // light tint via hex isn't possible, use a fixed light colour
                lc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                lc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                lc.Style.Alignment.Indent     = 1;
                // Use proper light tints per level
                var rowBg = lvl == "X" ? XLColor.FromHtml("#f7fee7") :
                            lvl == "I" ? XLColor.FromHtml("#f0fdf4") :
                            lvl == "L" ? XLColor.FromHtml("#fdf2f8") :
                            lvl == "U" ? XLColor.FromHtml("#fffbeb") :
                                         XLColor.FromHtml("#eff6ff");
                lc.Style.Fill.BackgroundColor = rowBg;

                for (int mi = 0; mi < ordered.Count; mi++)
                {
                    var dc = mws.Cell(dataRow, 2 + mi);
                    dc.Value = getter(ordered[mi]);
                    dc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    dc.Style.Fill.BackgroundColor = rowBg;
                    dc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    dc.Style.Font.FontColor       = XLColor.FromHtml(fgHex);
                    if (ordered[mi].MonthSort == nowSort)
                        dc.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                }
                dataRow++;
            }

            // ── Total row ──
            mws.Row(dataRow).Height = 19;
            var tc = mws.Cell(dataRow, 1);
            tc.Value = "Total";
            tc.Style.Font.Bold            = true;
            tc.Style.Font.FontColor       = XLColor.White;
            tc.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
            tc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            tc.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            for (int mi = 0; mi < ordered.Count; mi++)
            {
                var dc = mws.Cell(dataRow, 2 + mi);
                dc.Value = ordered[mi].Total;
                dc.Style.Font.Bold            = true;
                dc.Style.Font.FontColor       = XLColor.White;
                dc.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a5f");
                dc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                dc.Style.Border.OutsideBorder = ordered[mi].MonthSort == nowSort
                    ? XLBorderStyleValues.Medium : XLBorderStyleValues.Thin;
            }

            // ── Column widths ──
            mws.Column(1).Width = 16;
            for (int c = 2; c <= totalMonthCols; c++) mws.Column(c).Width = 10;

            // ── Freeze header ──
            mws.SheetView.FreezeRows(2);
        }

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        ms.Position = 0;

        var fileName = $"ILUO_SkillLevel_{DateTime.Now:yyyy-MM-dd}.xlsx";
        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }
}
