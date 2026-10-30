using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages.DevTools;

[DeveloperOnly]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[RequestSizeLimit(72 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 72 * 1024 * 1024)]
public sealed class SkillRecoveryModel(SkillRecoveryService service, DeveloperRoleService developers,
    TimeProvider clock, ILogger<SkillRecoveryModel> logger) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Query { get; set; }
    [BindProperty(SupportsGet = true)] public int SearchPage { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public string? EmpCode { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? RowId { get; set; }
    [BindProperty(SupportsGet = true)] public SkillRecoveryTable Table { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; } = "all";
    [BindProperty(SupportsGet = true)] public string? TargetQuery { get; set; }
    [BindProperty(SupportsGet = true)] public string? TargetCode { get; set; }
    [BindProperty(SupportsGet = true)] public string? Choice { get; set; }
    [BindProperty(SupportsGet = true)] public int HistoryPageNumber { get; set; } = 1;
    [BindProperty, ValidateNever] public SkillRecoveryRequest Input { get; set; } = new();
    [BindProperty] public string? PreviewToken { get; set; }
    [BindProperty] public bool ConfirmApply { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? UndoActionId { get; set; }
    [BindProperty] public string? UndoReason { get; set; }
    [BindProperty] public IFormFile? ReplacementPdf { get; set; }
    [TempData] public string? StatusMessage { get; set; }
    public bool HasError { get; private set; }
    public bool Loaded { get; private set; }
    public SkillRecoverySearch Employees { get; private set; } = new([], 1, false);
    public SkillRecoverySearch TargetEmployees { get; private set; } = new([], 1, false);
    public SkillRecoveryWorkspace Workspace { get; private set; } = new(null, []);
    public SkillRecoveryWorkspace TargetWorkspace { get; private set; } = new(null, []);
    public SkillRecoveryHistoryPage History { get; private set; } = new([], 1, false);
    public SkillRecoveryPreview? Preview { get; private set; }
    public SkillRecoveryPdfUploadInfo? StagedPdf { get; private set; }
    private bool _dialogRequested;
    public bool OpenDialog => _dialogRequested || RowId.HasValue || UndoActionId.HasValue || Preview != null;
    public string DialogReturnFocusId => UndoActionId.HasValue ? "recovery-undo-" + UndoActionId.Value.ToString("D") :
        RowId.HasValue ? "recovery-actions-" + Table + "-" + RowId.Value.ToString("D") : "recovery-source-employee";
    public SkillRecoveryRow? Selected => Workspace.Rows.FirstOrDefault(row => row.RowId == RowId && row.Table == Table);
    public IEnumerable<SkillRecoveryRow> VisibleRows => Workspace.Rows.Where(row => Filter switch
    {
        "current" => Status(row) == "Current",
        "disqualified" => row.Table == SkillRecoveryTable.Current && SkillRecoveryRules.IsDisqualified(row) && !SkillRecoveryRules.IsPromotion(row),
        "obsolete" => row.Table == SkillRecoveryTable.Archive,
        _ => true
    });
    public IEnumerable<SkillRecoveryRow> PriorRows => Selected == null ? [] : Workspace.Rows.Where(row =>
        row.Table == SkillRecoveryTable.Archive && !SkillRecoveryRules.IsPromotion(row) &&
        string.Equals(row.ProcessName, Selected.ProcessName, StringComparison.OrdinalIgnoreCase));
    public bool CanApply => !string.IsNullOrEmpty(Preview?.Token);
    private string Actor => HttpContext.Session.GetString("UserCode") ?? "";

    public async Task<IActionResult> OnGetAsync()
    {
        var denied = Guard(); if (denied != null) return denied;
        NormalizeFilter();
        if (Filter is not ("all" or "current" or "disqualified" or "obsolete") || !Enum.IsDefined(Table)) return BadRequest();
        await LoadAsync();
        if (Selected is { } selected)
        {
            Input = new SkillRecoveryRequest
            {
                EmployeeCode = selected.EmployeeCode, SourceTable = selected.Table, SourceRowId = selected.RowId,
                TargetEmployeeCode = TargetCode, Action = ValidChoice(Choice) &&
                    (!SkillRecoveryRules.IsPromotion(selected) || IsPdfAction(Choice)) ? Choice! : DefaultAction(selected),
                Reason = selected.Values.Remark ?? ""
            };
        }
        return Guard() ?? Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        var denied = Guard(); if (denied != null) return denied;
        _dialogRequested = true;
        NormalizeFilter();
        SynchronizeSelection();
        if (!ModelState.IsValid)
        {
            StatusMessage = "Check your selected record and fields, then preview again."; HasError = true;
        }
        else await PreviewAsync(Input);
        await LoadAsync();
        DescribeStagedPdf();
        return Guard() ?? Page();
    }

    public async Task<IActionResult> OnPostUndoAsync()
    {
        var denied = Guard(); if (denied != null) return denied;
        _dialogRequested = true;
        NormalizeFilter();
        if (!ModelState.IsValid || UndoActionId == null || UndoActionId == Guid.Empty)
        {
            StatusMessage = "Select a recovery action to preview."; HasError = true;
        }
        else
        {
            Input = new SkillRecoveryRequest { Action = "undo", EmployeeCode = EmpCode ?? "", ReversesActionId = UndoActionId, Reason = UndoReason ?? "" };
            await PreviewAsync(Input);
        }
        await LoadAsync();
        return Guard() ?? Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        var denied = Guard(); if (denied != null) return denied;
        _dialogRequested = true;
        NormalizeFilter();
        try
        {
            if (!ModelState.IsValid) throw new ArgumentException("Check the confirmation and preview, then try again.");
            var result = await service.ApplyAsync(Actor, HttpContext.Session.Id, PreviewToken, ConfirmApply, HttpContext.RequestAborted);
            StatusMessage = result.Message; HasError = !result.Success;
            if (result.Success)
                return RedirectToPage(new { EmpCode, Query, Filter, Recovery = result.ActionId });
        }
        catch (UnauthorizedAccessException) { return StatusCode(403); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (ArgumentException error) { StatusMessage = error.Message; HasError = true; }
        catch (Exception error)
        {
            logger.LogError(error, "Skill recovery apply failed for Developer {Actor}", Actor);
            StatusMessage = "Recovery could not be completed. Reload the records and review them before trying again."; HasError = true;
        }
        if (IsPdfAction(Input.Action)) SynchronizeSelection();
        await LoadAsync();
        DescribeStagedPdf();
        return Guard() ?? Page();
    }

    private async Task PreviewAsync(SkillRecoveryRequest request)
    {
        try
        {
            if (request.Action == "replace-pdf" && ReplacementPdf != null)
            {
                StagedPdf = await service.StagePdfAsync(Actor, HttpContext.Session.Id,
                    new(request.EmployeeCode, request.SourceTable, request.SourceRowId), ReplacementPdf, HttpContext.RequestAborted);
                request = request with { PdfUploadId = StagedPdf.UploadId };
                Input = request;
                ModelState.Remove("Input.PdfUploadId");
            }
            var result = await service.PreviewAsync(Actor, HttpContext.Session.Id, request, HttpContext.RequestAborted);
            StatusMessage = result.Message; HasError = !result.Success; Preview = result.Preview;
            StagedPdf = Preview?.Plan.Context.ReplacementPdf ?? StagedPdf;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Skill recovery preview failed for Developer {Actor}", Actor);
            StatusMessage = error switch
            {
                InvalidDataException => "Choose a valid PDF file within the allowed size, then preview again.",
                FileNotFoundException => "The selected PDF is unavailable or expired. Choose the file again and preview.",
                ArgumentException or SkillRecoveryConflictException => error.Message,
                _ => "The preview is unavailable. Please try again."
            };
            HasError = true;
        }
    }

    private void DescribeStagedPdf()
    {
        if (StagedPdf != null || Input.Action != "replace-pdf" || Input.PdfUploadId is not { } uploadId) return;
        try
        {
            StagedPdf = service.DescribePdfUpload(Actor, HttpContext.Session.Id,
                new(Input.EmployeeCode, Input.SourceTable, Input.SourceRowId), uploadId);
        }
        catch (Exception error) when (error is ArgumentException or SkillRecoveryConflictException or UnauthorizedAccessException or FileNotFoundException)
        {
            Input = Input with { PdfUploadId = null };
            ModelState.Remove("Input.PdfUploadId");
            StatusMessage = "The selected PDF is unavailable or expired. Choose the file again and preview.";
            HasError = true;
        }
    }

    private async Task LoadAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(EmpCode)) Workspace = await service.WorkspaceAsync(Actor, EmpCode, HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(Query) || Workspace.Employee == null)
                Employees = await service.SearchEmployeesAsync(Actor, Query, SearchPage, HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(TargetQuery))
                TargetEmployees = await service.SearchEmployeesAsync(Actor, TargetQuery, 1, HttpContext.RequestAborted);
            if (!string.IsNullOrWhiteSpace(TargetCode))
                TargetWorkspace = await service.WorkspaceAsync(Actor, TargetCode, HttpContext.RequestAborted);
            History = await service.HistoryAsync(Actor, EmpCode, HistoryPageNumber, HttpContext.RequestAborted);
            Loaded = true;
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            logger.LogError(error, "Skill Recovery records unavailable for Developer {Actor}", Actor);
            StatusMessage = error is ArgumentException ? error.Message : "Skill records or recovery history could not be loaded. Please try again.";
            HasError = true;
        }
    }

    private IActionResult? Guard() => string.IsNullOrWhiteSpace(Actor) ? RedirectToPage("/Login") : developers.IsDeveloper(Actor) ? null : StatusCode(403);
    private void NormalizeFilter()
    {
        if (!string.IsNullOrWhiteSpace(Filter)) return;
        Filter = "all";
        ModelState.Remove(nameof(Filter));
    }
    private void SynchronizeSelection() { EmpCode = Input.EmployeeCode; RowId = Input.SourceRowId; Table = Input.SourceTable; TargetCode = Input.TargetEmployeeCode; }
    private static bool IsPdfAction(string? action) => action is "remove-pdf" or "replace-pdf";
    private static bool ValidChoice(string? choice) => choice is "reinstate" or "restore" or "move" or "undo-edit" or "undo-add" or "move-tab" or "remove-pdf" or "replace-pdf";
    private string DefaultAction(SkillRecoveryRow row) => SkillRecoveryRules.IsPromotion(row) ? "replace-pdf" : row.Table == SkillRecoveryTable.Archive ? "restore" :
        !string.IsNullOrWhiteSpace(row.Values.DisQualifiedBy) ? "reinstate" : PriorRows.Any() ? "undo-edit" : "undo-add";
    public string Status(SkillRecoveryRow row) => SkillRecoveryRules.Status(row, clock.GetLocalNow().DateTime);
    public string ActionLabel(string action) => SkillRecoveryRules.Label(action);
    public string Date(DateTime? value) => value?.ToString("dd MMM yyyy") ?? "—";
    public bool HasPdf(SkillRecoveryRow row) => row.Values.CertificateFileId.HasValue || !string.IsNullOrWhiteSpace(row.Values.Download);
    public string PdfUrl(SkillRecoveryRow row) => "/api/skill-recovery/file/" + Uri.EscapeDataString(row.EmployeeCode) + "/" + row.RowId.ToString("D") + (row.Table == SkillRecoveryTable.Archive ? "?obsolete=true" : "");
    public string UploadPdfUrl(SkillRecoveryPdfUploadInfo upload) => "/api/skill-recovery/upload/" + upload.UploadId.ToString("D") + "/file";
    public string PdfSize(long bytes) => bytes >= 1024 * 1024 ? (bytes / (1024d * 1024d)).ToString("0.##") + " MB" : (bytes / 1024d).ToString("0.#") + " KB";
    public string? RemovedSourcePdfUrl
    {
        get
        {
            if (Preview?.Plan.Request is not { Action: "undo", ReversesActionId: { } actionId } || actionId == Guid.Empty ||
                Preview.Plan.Context.Source is not { } source || !HasPdf(source)) return null;
            if (Preview.Plan.Context.OriginalAction is not ("remove-pdf" or "replace-pdf") &&
                (Preview.Plan.Context.OriginalAction != "undo-add" ||
                 Preview.Plan.Context.UndoBeforeRows is not { } before || before.Any(row => row.Key == source.Key) ||
                 Preview.Plan.Context.UndoAfterRows is not { } after || !after.Any(row => row.Key == source.Key))) return null;
            return "/api/skill-recovery/history/" + actionId.ToString("D") + "/file";
        }
    }
    public bool ActionAvailable(string action) => Selected is { } row && Workspace.Employee?.Current == true &&
        (!SkillRecoveryRules.IsPromotion(row) || IsPdfAction(action)) && (action switch
    {
        "reinstate" => row.Table == SkillRecoveryTable.Current && !string.IsNullOrWhiteSpace(row.Values.DisQualifiedBy),
        "restore" => row.Table == SkillRecoveryTable.Archive,
        "move" => true,
        "move-tab" => true,
        "undo-edit" => row.Table == SkillRecoveryTable.Current && PriorRows.Any(),
        "undo-add" => row.Table is SkillRecoveryTable.Current or SkillRecoveryTable.Archive,
        "remove-pdf" => HasPdf(row),
        "replace-pdf" => true,
        _ => false
    });

    public int TabCount(string tab) => Workspace.Rows.Count(row => tab switch
    {
        "current" => Status(row) == "Current",
        "disqualified" => row.Table == SkillRecoveryTable.Current && SkillRecoveryRules.IsDisqualified(row) && !SkillRecoveryRules.IsPromotion(row),
        "obsolete" => row.Table == SkillRecoveryTable.Archive,
        _ => true
    });
    public string RecordTab(SkillRecoveryRow row) => row.Table == SkillRecoveryTable.Archive ? "Obsolete" :
        SkillRecoveryRules.IsDisqualified(row) ? "Disqualified" : "Current";
    public bool TargetTabAvailable(string target) => Selected is { } row &&
        !string.Equals(RecordTab(row), target, StringComparison.OrdinalIgnoreCase) &&
        (target != "current" || Status(row with { Table = SkillRecoveryTable.Current,
            Values = row.Values with { DisQualifiedBy = null, DisQualifiedDate = null, TheReason = null } }) == "Current");

    public IEnumerable<(string Label, string Before, string After)> Comparison()
    {
        if (Preview == null) yield break;
        if (Preview.Plan.Request.Action == "undo")
        {
            var beforeRows = Preview.Plan.Context.UndoBeforeRows ?? [];
            var afterRows = Preview.Plan.Context.UndoAfterRows ?? [];
            var scopes = beforeRows.Concat(afterRows).Select(row => (row.EmployeeCode, row.ProcessName)).Distinct();
            foreach (var scope in scopes)
            {
                yield return (scope.EmployeeCode + " · " + scope.ProcessName,
                    SnapshotText(beforeRows.Where(row => row.EmployeeCode == scope.EmployeeCode && row.ProcessName == scope.ProcessName)),
                    SnapshotText(afterRows.Where(row => row.EmployeeCode == scope.EmployeeCode && row.ProcessName == scope.ProcessName)));
            }
            if (!scopes.Any()) yield return ("Skill records", "None", "None");
            yield break;
        }
        var source = Preview.Plan.Context.Source;
        var after = Preview.Plan.Request.Action == "undo-edit" ? Preview.Plan.Context.Prior : source;
        var a = source?.Values; var b = after?.Values;
        if (source != null && Preview.Plan.Request.Action == "move-tab")
            b = SkillRecoveryRules.ResultValues(Preview.Plan.Request, source.Values, Preview.Plan.Actor, clock.GetLocalNow().DateTime);
        string Text(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
        yield return ("Employee", Preview.Plan.Context.SourceEmployee.Name + " · " + Preview.Plan.Context.SourceEmployee.EmployeeCode,
            Preview.Plan.Context.TargetEmployee is { } target ? target.Name + " · " + target.EmployeeCode : Preview.Plan.Context.SourceEmployee.Name + " · " + Preview.Plan.Context.SourceEmployee.EmployeeCode);
        if (source == null) yield break;
        if (IsPdfAction(Preview.Plan.Request.Action))
        {
            yield return ("PDF", HasPdf(source) ? source.Values.CertificateFileId.HasValue ? "SQL certificate" : "Saved legacy reference" : "Not linked",
                Preview.Plan.Request.Action == "remove-pdf" ? "Not linked" : Preview.Plan.Context.ReplacementPdf?.FileName ?? "Replacement PDF");
            yield return ("Skill fields", "Existing values", "Unchanged");
            yield return ("Remark", Text(a?.Remark), Text(a?.Remark));
            yield break;
        }
        if (Preview.Plan.Request.Action == "move-tab")
            yield return ("Tab", RecordTab(source), SkillRecoveryRules.TabLabel(Preview.Plan.Request.TargetTab ?? ""));
        if (Preview.Plan.Request.Action == "undo-add") { yield return ("State", Status(source), "Removed from skill records"); yield break; }
        yield return ("Process", source.ProcessName, after?.ProcessName ?? "—");
        yield return ("Classification", Text(a?.OperatorTraining), Text(b?.OperatorTraining));
        yield return ("Theory", Date(a?.TheoryTraining), Date(b?.TheoryTraining));
        yield return ("OJT", Date(a?.OJTTraining), Date(b?.OJTTraining));
        yield return ("Certified", Date(a?.CertifiedDate), Date(b?.CertifiedDate));
        yield return ("Expires", Date(a?.ExpiryDate), Date(b?.ExpiryDate));
        yield return ("Full score", Text(a?.FullScore), Text(b?.FullScore));
        yield return ("Actual score", Text(a?.ActualScore), Text(b?.ActualScore));
        yield return ("Test result", Text(a?.TestResult), Text(b?.TestResult));
        yield return ("Theory result", Text(a?.JudgmentTheory), Text(b?.JudgmentTheory));
        yield return ("Knowledge", Text(a?.KnowledgeScore) + " / " + Text(a?.KnowledgeLevel), Text(b?.KnowledgeScore) + " / " + Text(b?.KnowledgeLevel));
        yield return ("Skill", Text(a?.SkillScore) + " / " + Text(a?.SkillLevel), Text(b?.SkillScore) + " / " + Text(b?.SkillLevel));
        yield return ("Practice result", Text(a?.JudgmentPractice), Text(b?.JudgmentPractice));
        yield return ("Verifier", Text(a?.Verifier), Text(b?.Verifier));
        yield return ("Verified", Date(a?.VerifierDate), Date(b?.VerifierDate));
        yield return ("Disqualified by", Text(a?.DisQualifiedBy), Preview.Plan.Request.Action == "reinstate" ? "Cleared" : Text(b?.DisQualifiedBy));
        yield return ("Disqualified", Date(a?.DisQualifiedDate), Preview.Plan.Request.Action == "reinstate" ? "Cleared" : Date(b?.DisQualifiedDate));
        yield return ("Disqualification reason", Text(a?.TheReason), Preview.Plan.Request.Action == "reinstate" ? "Cleared" : Text(b?.TheReason));
        yield return ("Remark", Text(a?.Remark), Text(b?.Remark));
        yield return ("PDF", HasPdf(source) ? source.Values.CertificateFileId.HasValue ? "SQL certificate" : "Saved legacy reference" : "Not linked",
            after != null && HasPdf(after) ? Preview.Plan.Request.Action == "move" ? "Copied for destination" :
            after.Values.CertificateFileId.HasValue ? "Selected version's certificate" : "Saved legacy reference" : "Not linked");
    }

    private string SnapshotText(IEnumerable<SkillRecoveryRow> rows)
    {
        var snapshots = rows.Select(row =>
        {
            var value = row.Values;
            string Text(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text;
            return $"{Status(row)} · {Text(value.OperatorTraining)}\nTheory: {Date(value.TheoryTraining)} · OJT: {Date(value.OJTTraining)}\n" +
                $"Certified: {Date(value.CertifiedDate)} · Expires: {Date(value.ExpiryDate)}\n" +
                $"Full / actual / test: {Text(value.FullScore)} / {Text(value.ActualScore)} / {Text(value.TestResult)}\n" +
                $"Theory result: {Text(value.JudgmentTheory)} · Practice: {Text(value.JudgmentPractice)}\n" +
                $"Knowledge: {Text(value.KnowledgeScore)} / {Text(value.KnowledgeLevel)} · Skill: {Text(value.SkillScore)} / {Text(value.SkillLevel)}\n" +
                $"Verifier: {Text(value.Verifier)} · Verified: {Date(value.VerifierDate)}\n" +
                $"Disqualified: {Date(value.DisQualifiedDate)} · By: {Text(value.DisQualifiedBy)}\n" +
                $"Disqualification reason: {Text(value.TheReason)}\nRemark: {Text(value.Remark)}\nPDF: {(value.CertificateFileId.HasValue ? "SQL certificate retained" : HasPdf(row) ? "Saved legacy reference" : "Not linked")}";
        }).ToArray();
        return snapshots.Length == 0 ? "No skill version" : string.Join("\n\n", snapshots);
    }
}
