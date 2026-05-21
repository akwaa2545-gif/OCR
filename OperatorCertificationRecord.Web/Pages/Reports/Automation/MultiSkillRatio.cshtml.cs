using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.Reports.Automation;

[AdminOnly]
public class MultiSkillRatioModel : PageModel
{
    private readonly ReportAutomationService _svc;
    private readonly ILogger<MultiSkillRatioModel> _log;

    public List<MultiSkillRatioRow> Rows { get; set; } = new();
    public List<MultiSkillMonthRow> MonthRows { get; set; } = new();

    public MultiSkillRatioModel(ReportAutomationService svc, ILogger<MultiSkillRatioModel> log)
    {
        _svc = svc;
        _log = log;
    }

    public async Task OnGetAsync()
    {
        try
        {
            var rowsTask  = _svc.GetMultiSkillRatioAsync();
            var monthTask = _svc.GetMultiSkillRatioByMonthAsync();
            await Task.WhenAll(rowsTask, monthTask);
            Rows      = rowsTask.Result;
            MonthRows = monthTask.Result;
        }
        catch (Exception ex) { _log.LogError(ex, "MultiSkillRatio load error"); }
    }

    public async Task<IActionResult> OnPostDownloadAsync()
    {
        try
        {
            var rowsTask  = _svc.GetMultiSkillRatioAsync();
            var monthTask = _svc.GetMultiSkillRatioByMonthAsync();
            await Task.WhenAll(rowsTask, monthTask);
            Rows      = rowsTask.Result;
            MonthRows = monthTask.Result;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MultiSkillRatio download load error");
            if (!Rows.Any())
                try { Rows = await _svc.GetMultiSkillRatioAsync(); } catch { }
        }

        // ── colour palette ───────────────────────────────────────────────────
        var cNavy    = XLColor.FromHtml("#1e3a5f");
        var cSteelBg = XLColor.FromHtml("#e8f0fe");
        var cGreenBg = XLColor.FromHtml("#d1fae5");
        var cGreenFg = XLColor.FromHtml("#065f46");
        var cRedBg   = XLColor.FromHtml("#fee2e2");
        var cRedFg   = XLColor.FromHtml("#991b1b");
        var cAmberBg = XLColor.FromHtml("#fef9c3");
        var cAmberFg = XLColor.FromHtml("#78350f");
        var cTotalBg = XLColor.FromHtml("#1e3a5f");
        var cRowAlt  = XLColor.FromHtml("#f8fafc");
        var cRowNorm = XLColor.White;

        // helper: apply thin outline border to a range
        void BorderRange(IXLRange rng)
        {
            rng.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            rng.Style.Border.InsideBorder   = XLBorderStyleValues.Thin;
        }

        using var wb = new XLWorkbook();

        // ════════════════════════════════════════════════════════════════════
        // SHEET 1 — Current Snapshot
        // ════════════════════════════════════════════════════════════════════
        var ws = wb.Worksheets.Add("Multi-skill Ratio");
        ws.SheetView.FreezeRows(2);   // freeze header

        // ── Title bar ──
        ws.Row(1).Height = 28;
        var titleCell = ws.Cell(1, 1);
        titleCell.Value = $"Multi-skill Ratio  ·  As of {DateTime.Now:dd MMMM yyyy}";
        titleCell.Style.Font.Bold      = true;
        titleCell.Style.Font.FontSize  = 14;
        titleCell.Style.Font.FontColor = XLColor.White;
        titleCell.Style.Fill.BackgroundColor = cNavy;
        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        titleCell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
        ws.Range(1, 1, 1, 7).Merge();

        // ── Column headers ──
        ws.Row(2).Height = 22;
        var headers = new[] { "Workshop / Area", "None Skill", "1 - Skill",
                               "2+ Skill Up", "Total Operator", "Ratio 2+ (%)", "Balance" };
        for (int c = 0; c < headers.Length; c++)
        {
            var hc = ws.Cell(2, c + 1);
            hc.Value = headers[c];
            hc.Style.Font.Bold      = true;
            hc.Style.Font.FontColor = XLColor.White;
            hc.Style.Fill.BackgroundColor  = XLColor.FromHtml("#2d5282");
            hc.Style.Alignment.Horizontal  = XLAlignmentHorizontalValues.Center;
            hc.Style.Alignment.Vertical    = XLAlignmentVerticalValues.Center;
            hc.Style.Alignment.WrapText    = true;
            hc.Style.Border.OutsideBorder  = XLBorderStyleValues.Thin;
            hc.Style.Border.BottomBorder   = XLBorderStyleValues.Medium;
        }

        // ── Data rows ──
        int row = 3;
        int totNone = 0, totOne = 0, totTwo = 0;
        bool alt = false;
        foreach (var r in Rows)
        {
            var rowBg = alt ? cRowAlt : cRowNorm;
            alt = !alt;

            ws.Cell(row, 1).Value = r.WorkshopName;
            ws.Cell(row, 2).Value = r.NoneSkill;
            ws.Cell(row, 3).Value = r.OneSkill;
            ws.Cell(row, 4).Value = r.TwoSkillUp;
            ws.Cell(row, 5).Value = r.TotalOperator;
            ws.Cell(row, 6).Value = r.RatioTwoUp;
            ws.Cell(row, 7).Value = r.TwoSkillUp;

            for (int c = 1; c <= 7; c++)
            {
                var dc = ws.Cell(row, c);
                dc.Style.Fill.BackgroundColor = rowBg;
                dc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                dc.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            }
            // Workshop name: left-align
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(row, 1).Style.Alignment.Indent = 1;

            // Numbers: centre
            for (int c = 2; c <= 7; c++)
                ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // None-skill: highlight if > 0
            if (r.NoneSkill > 0)
            {
                ws.Cell(row, 2).Style.Fill.BackgroundColor = cRedBg;
                ws.Cell(row, 2).Style.Font.FontColor       = cRedFg;
                ws.Cell(row, 2).Style.Font.Bold            = true;
            }

            // Ratio 2+ conditional colour
            double ratio = r.RatioTwoUp;
            ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";
            if (ratio >= 85)
            {
                ws.Cell(row, 6).Style.Fill.BackgroundColor = cGreenBg;
                ws.Cell(row, 6).Style.Font.FontColor       = cGreenFg;
                ws.Cell(row, 6).Style.Font.Bold            = true;
            }
            else if (ratio >= 70)
            {
                ws.Cell(row, 6).Style.Fill.BackgroundColor = cAmberBg;
                ws.Cell(row, 6).Style.Font.FontColor       = cAmberFg;
                ws.Cell(row, 6).Style.Font.Bold            = true;
            }
            else
            {
                ws.Cell(row, 6).Style.Fill.BackgroundColor = cRedBg;
                ws.Cell(row, 6).Style.Font.FontColor       = cRedFg;
                ws.Cell(row, 6).Style.Font.Bold            = true;
            }

            // 2+ Skill column: always green tint
            ws.Cell(row, 4).Style.Fill.BackgroundColor = cGreenBg;
            ws.Cell(row, 4).Style.Font.FontColor       = cGreenFg;

            totNone += r.NoneSkill; totOne += r.OneSkill; totTwo += r.TwoSkillUp;
            row++;
        }

        // ── Total row ──
        int totTotal = totNone + totOne + totTwo;
        double totRatio = totTotal > 0 ? Math.Round((double)totTwo / totTotal * 100, 2) : 0;
        ws.Row(row).Height = 20;
        var totNums   = new double[] { 0, totNone, totOne, totTwo, totTotal, totRatio, totTwo };
        for (int c = 0; c < 7; c++)
        {
            var tc = ws.Cell(row, c + 1);
            if (c == 0) tc.Value = "Grand Total";
            else        tc.Value = totNums[c];
            tc.Style.Font.Bold            = true;
            tc.Style.Font.FontColor       = XLColor.White;
            tc.Style.Fill.BackgroundColor = cTotalBg;
            tc.Style.Alignment.Horizontal = c == 0 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Center;
            tc.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            tc.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
        }
        ws.Cell(row, 1).Style.Alignment.Indent = 1;
        ws.Cell(row, 6).Style.NumberFormat.Format = "0.00";
        // Ratio total colour
        var ratioCell = ws.Cell(row, 6);
        ratioCell.Style.Fill.BackgroundColor = totRatio >= 85 ? XLColor.FromHtml("#166534") : XLColor.FromHtml("#7f1d1d");

        // ── Column widths ──
        ws.Column(1).Width = 30;
        for (int c = 2; c <= 7; c++) ws.Column(c).Width = 14;

        // ── Legend (2 rows below data) ──
        int legendRow = row + 2;
        ws.Cell(legendRow, 1).Value = "Colour Legend";
        ws.Cell(legendRow, 1).Style.Font.Bold = true;
        ws.Cell(legendRow, 1).Style.Font.FontColor = XLColor.FromHtml("#374151");
        var legends = new[] {
            ("≥ 85%  (On Target)",  "#d1fae5", "#065f46"),
            ("70–85% (Near Target)", "#fef9c3", "#78350f"),
            ("< 70%  (Below Target)","#fee2e2", "#991b1b"),
        };
        for (int i = 0; i < legends.Length; i++)
        {
            var lc = ws.Cell(legendRow + 1 + i, 1);
            lc.Value = legends[i].Item1;
            lc.Style.Fill.BackgroundColor = XLColor.FromHtml(legends[i].Item2);
            lc.Style.Font.FontColor       = XLColor.FromHtml(legends[i].Item3);
            lc.Style.Font.Bold            = true;
            lc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        // ════════════════════════════════════════════════════════════════════
        // SHEET 2 — By Month pivot
        // ════════════════════════════════════════════════════════════════════
        if (MonthRows.Any())
        {
            var mws = wb.Worksheets.Add("By Month");
            int nowSort2 = DateTime.Now.Year * 100 + DateTime.Now.Month;
            const double plan = 85.0;

            var months2 = MonthRows.Select(r => (r.MonthSort, r.MonthLabel))
                                   .Distinct().OrderBy(m => m.MonthSort).ToList();
            var workshops2 = MonthRows.Select(r => r.WorkshopName)
                                      .Distinct().OrderBy(x => x).ToList();
            var lookup = MonthRows
                .GroupBy(r => (r.WorkshopName, r.MonthSort))
                .ToDictionary(g => g.Key, g => g.First());

            int totalCols = 2 + months2.Count;

            // ── Freeze first 3 rows + first 2 columns ──
            mws.SheetView.Freeze(3, 2);

            // ── Title ──
            mws.Row(1).Height = 26;
            var mTitle = mws.Cell(1, 1);
            mTitle.Value = $"Multi-skill Ratio by Month  ·  As of {DateTime.Now:dd MMMM yyyy}";
            mTitle.Style.Font.Bold            = true;
            mTitle.Style.Font.FontSize        = 13;
            mTitle.Style.Font.FontColor       = XLColor.White;
            mTitle.Style.Fill.BackgroundColor = cNavy;
            mTitle.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mTitle.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            mws.Range(1, 1, 1, totalCols).Merge();

            // ── Row 2: group header "Workshop / Item" + "Number of Operators" ──
            mws.Row(2).Height = 20;
            void HdrCell2(IXLCell c, string val, string bgHex)
            {
                c.Value = val;
                c.Style.Font.Bold            = true;
                c.Style.Font.FontColor       = XLColor.White;
                c.Style.Fill.BackgroundColor = XLColor.FromHtml(bgHex);
                c.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                c.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                c.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            HdrCell2(mws.Cell(2, 1), "Workshop / Area", "#2d5282");
            HdrCell2(mws.Cell(2, 2), "Item",            "#2d5282");
            mws.Range(2, 1, 3, 1).Merge();
            mws.Range(2, 2, 3, 2).Merge();
            mws.Cell(2, 3).Value = "Number of Operators per Month";
            mws.Cell(2, 3).Style.Font.Bold            = true;
            mws.Cell(2, 3).Style.Font.FontColor       = XLColor.White;
            mws.Cell(2, 3).Style.Fill.BackgroundColor = XLColor.FromHtml("#2d5282");
            mws.Cell(2, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mws.Cell(2, 3).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            if (months2.Count > 1) mws.Range(2, 3, 2, totalCols).Merge();

            // ── Row 3: month label headers ──
            mws.Row(3).Height = 18;
            for (int mi = 0; mi < months2.Count; mi++)
            {
                var (mSort, mLbl) = months2[mi];
                var hc = mws.Cell(3, 3 + mi);
                hc.Value = mLbl;
                hc.Style.Font.Bold            = true;
                hc.Style.Font.FontColor       = XLColor.White;
                hc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                hc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                // Current month: accent colour
                hc.Style.Fill.BackgroundColor = mSort == nowSort2
                    ? XLColor.FromHtml("#b45309")   // amber-700
                    : mSort > nowSort2
                        ? XLColor.FromHtml("#475569") // slate-600 = future
                        : XLColor.FromHtml("#334155"); // slate-700 = past
            }

            // ── Data rows ──
            int dataRow = 4;
            var subItems = new[] { ("None skill", 0, "#fef2f2", "#991b1b"),
                                   ("1 - Skill",  1, "#fefce8", "#78350f"),
                                   ("2 - Skill up",2,"#f0fdf4", "#065f46") };

            foreach (var wsName in workshops2)
            {
                int spanStart = dataRow;

                for (int si = 0; si < subItems.Length; si++)
                {
                    var (label, tier, bgHex, fgHex) = subItems[si];
                    var bg = XLColor.FromHtml(bgHex);
                    var fg = XLColor.FromHtml(fgHex);

                    mws.Row(dataRow).Height = 16;

                    // Col A — workshop name (merged over 3 sub-rows)
                    if (si == 0)
                    {
                        var wc = mws.Cell(dataRow, 1);
                        wc.Value = wsName;
                        wc.Style.Font.Bold            = true;
                        wc.Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");
                        wc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                        wc.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                        wc.Style.Alignment.Indent     = 1;
                        wc.Style.Border.LeftBorder    = XLBorderStyleValues.Medium;
                        wc.Style.Border.LeftBorderColor = XLColor.FromHtml("#f97316"); // orange accent
                        mws.Range(spanStart, 1, spanStart + 2, 1).Merge();
                    }

                    // Col B — item label
                    var lc = mws.Cell(dataRow, 2);
                    lc.Value = label;
                    lc.Style.Font.Bold            = true;
                    lc.Style.Font.FontColor       = fg;
                    lc.Style.Fill.BackgroundColor = bg;
                    lc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                    lc.Style.Alignment.Indent     = 1;
                    lc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                    // Data cells
                    for (int mi = 0; mi < months2.Count; mi++)
                    {
                        int val = 0;
                        if (lookup.TryGetValue((wsName, months2[mi].MonthSort), out var mrow))
                            val = tier == 0 ? mrow.NoneSkill : tier == 1 ? mrow.OneSkill : mrow.TwoSkillUp;

                        var dc = mws.Cell(dataRow, 3 + mi);
                        dc.Value = val;
                        dc.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        dc.Style.Fill.BackgroundColor = bg;
                        dc.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        // None-skill: bold red if > 0
                        if (tier == 0 && val > 0)
                        {
                            dc.Style.Font.FontColor = fg;
                            dc.Style.Font.Bold = true;
                        }
                        // Current month column: slightly darker tint
                        if (months2[mi].MonthSort == nowSort2)
                            dc.Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                    }
                    dataRow++;
                }
                // Thick bottom border after each workshop block
                for (int c = 1; c <= totalCols; c++)
                    mws.Cell(dataRow - 1, c).Style.Border.BottomBorder = XLBorderStyleValues.Medium;
            }

            // ── Footer section ──
            var fNone2 = months2.Select(m => MonthRows.Where(r => r.MonthSort == m.MonthSort).Sum(r => r.NoneSkill)).ToList();
            var fOne2  = months2.Select(m => MonthRows.Where(r => r.MonthSort == m.MonthSort).Sum(r => r.OneSkill)).ToList();
            var fTwo2  = months2.Select(m => MonthRows.Where(r => r.MonthSort == m.MonthSort).Sum(r => r.TwoSkillUp)).ToList();
            var fOps2  = months2.Select((_, i) => fNone2[i] + fOne2[i] + fTwo2[i]).ToList();

            // Separator line
            for (int c = 1; c <= totalCols; c++)
                mws.Cell(dataRow, c).Style.Border.TopBorder = XLBorderStyleValues.Medium;

            // helper: write a footer cell
            void Ft(IXLCell cell, string hex, string fgHex2 = "#1e293b", bool bold = false)
            {
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml(hex);
                cell.Style.Font.FontColor       = XLColor.FromHtml(fgHex2);
                cell.Style.Font.Bold            = bold;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            }

            // Total sub-rows
            var totalSubRows = new[] { ("None skill", fNone2, "#fef2f2", "#991b1b"),
                                       ("1 - Skill",  fOne2,  "#fefce8", "#78350f"),
                                       ("2 - Skill up",fTwo2, "#f0fdf4", "#065f46") };
            int totSpanStart = dataRow;
            foreach (var (lbl, vals, bgH, fgH) in totalSubRows)
            {
                mws.Cell(dataRow, 2).Value = lbl;
                Ft(mws.Cell(dataRow, 2), bgH, fgH, true);
                for (int mi = 0; mi < months2.Count; mi++) { mws.Cell(dataRow, 3 + mi).Value = vals[mi]; Ft(mws.Cell(dataRow, 3 + mi), bgH, fgH); }
                dataRow++;
            }
            // "Total" label merged over the 3 sub-rows
            mws.Cell(totSpanStart, 1).Value = "Total";
            mws.Range(totSpanStart, 1, totSpanStart + 2, 1).Merge();
            mws.Cell(totSpanStart, 1).Style.Font.Bold            = true;
            mws.Cell(totSpanStart, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
            mws.Cell(totSpanStart, 1).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            mws.Cell(totSpanStart, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mws.Cell(totSpanStart, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // Total Operator row
            mws.Cell(dataRow, 1).Value = "Total Operator";
            mws.Range(dataRow, 1, dataRow, 2).Merge();
            mws.Cell(dataRow, 1).Style.Font.Bold            = true;
            mws.Cell(dataRow, 1).Style.Font.FontColor       = XLColor.White;
            mws.Cell(dataRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1d4ed8");
            mws.Cell(dataRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            mws.Cell(dataRow, 1).Style.Alignment.Indent     = 1;
            mws.Cell(dataRow, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            for (int mi = 0; mi < months2.Count; mi++)
            {
                mws.Cell(dataRow, 3 + mi).Value = fOps2[mi];
                Ft(mws.Cell(dataRow, 3 + mi), "#dbeafe", "#1e3a8a", true);
            }
            dataRow++;

            // Multi-skill ratio Plan row
            int ratioSpan = dataRow;
            mws.Cell(dataRow, 2).Value = "Plan (%)";
            Ft(mws.Cell(dataRow, 2), "#fef3c7", "#78350f", true);
            for (int mi = 0; mi < months2.Count; mi++)
            {
                mws.Cell(dataRow, 3 + mi).Value = plan;
                mws.Cell(dataRow, 3 + mi).Style.NumberFormat.Format = "0.00";
                Ft(mws.Cell(dataRow, 3 + mi), "#fef3c7", "#78350f");
            }
            dataRow++;

            // Multi-skill ratio Actual row
            mws.Cell(dataRow, 2).Value = "Actual (%)";
            Ft(mws.Cell(dataRow, 2), "#f0fdf4", "#065f46", true);
            for (int mi = 0; mi < months2.Count; mi++)
            {
                double actual = months2[mi].MonthSort > nowSort2 ? 0.0
                              : fOps2[mi] > 0 ? Math.Round((double)fTwo2[mi] / fOps2[mi] * 100, 2) : 0.0;
                bool ok = actual >= plan && actual > 0;
                mws.Cell(dataRow, 3 + mi).Value = actual;
                mws.Cell(dataRow, 3 + mi).Style.NumberFormat.Format = "0.00";
                mws.Cell(dataRow, 3 + mi).Style.Font.Bold = true;
                Ft(mws.Cell(dataRow, 3 + mi), ok ? "#bbf7d0" : "#fee2e2", ok ? "#14532d" : "#991b1b");
                if (months2[mi].MonthSort == nowSort2)
                    mws.Cell(dataRow, 3 + mi).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
            }
            dataRow++;

            // "Multi-skill ratio" label merged over Plan+Actual
            mws.Cell(ratioSpan, 1).Value = "Multi-skill Ratio";
            mws.Range(ratioSpan, 1, ratioSpan + 1, 1).Merge();
            mws.Cell(ratioSpan, 1).Style.Font.Bold            = true;
            mws.Cell(ratioSpan, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#fef3c7");
            mws.Cell(ratioSpan, 1).Style.Font.FontColor       = XLColor.FromHtml("#78350f");
            mws.Cell(ratioSpan, 1).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            mws.Cell(ratioSpan, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            mws.Cell(ratioSpan, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

            // Operator out of control
            mws.Cell(dataRow, 1).Value = "Out of Control (No Skill)";
            mws.Range(dataRow, 1, dataRow, 2).Merge();
            mws.Cell(dataRow, 1).Style.Font.Bold            = true;
            mws.Cell(dataRow, 1).Style.Font.FontColor       = XLColor.FromHtml("#991b1b");
            mws.Cell(dataRow, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
            mws.Cell(dataRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            mws.Cell(dataRow, 1).Style.Alignment.Indent     = 1;
            mws.Cell(dataRow, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            for (int mi = 0; mi < months2.Count; mi++)
            {
                mws.Cell(dataRow, 3 + mi).Value = fNone2[mi];
                Ft(mws.Cell(dataRow, 3 + mi), "#fee2e2", "#991b1b", true);
            }

            // ── Column widths ──
            mws.Column(1).Width = 28;
            mws.Column(2).Width = 14;
            for (int c = 3; c <= totalCols; c++) mws.Column(c).Width = 10;
        }
    
        using var ms = new MemoryStream();
        wb.SaveAs(ms);

        return File(ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"MultiSkillRatio_{DateTime.Now:yyyy-MM-dd}.xlsx");
    }

}
