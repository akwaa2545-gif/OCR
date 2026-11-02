using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed class SkillRecoveryService
{
    private const int MaximumTokenLength = 180000;
    private readonly ISkillRecoveryStore _store;
    private readonly DeveloperRoleService _developers;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<SkillRecoveryService> _logger;
    private readonly SkillRecoveryPdfUploads? _uploads;

    public SkillRecoveryService(ISkillRecoveryStore store, DeveloperRoleService developers,
        IDataProtectionProvider protection, TimeProvider clock, ILogger<SkillRecoveryService> logger,
        SkillRecoveryPdfUploads? uploads = null)
    {
        _store = store; _developers = developers; _clock = clock; _logger = logger; _uploads = uploads;
        _protector = protection.CreateProtector("DevTools.SkillRecovery.Preview.v1");
    }

    public async Task<SkillRecoverySearch> SearchEmployeesAsync(string actor, string? query, int page, CancellationToken ct)
    {
        RequireDeveloper(actor);
        var clean = (query ?? "").Trim();
        if (clean.Length > 100 || clean.Any(char.IsControl) || !ValidPage(page)) throw new ArgumentException("Enter a search of at most 100 characters and a valid page.");
        var rows = await _store.SearchEmployeesAsync(clean, page, ct);
        RequireDeveloper(actor);
        return rows;
    }

    public async Task<SkillRecoveryWorkspace> WorkspaceAsync(string actor, string employeeCode, CancellationToken ct)
    {
        RequireDeveloper(actor);
        if (!SkillRecoveryRules.ValidCode(employeeCode)) throw new ArgumentException("Choose a valid employee.");
        var workspace = await _store.ReadWorkspaceAsync(employeeCode, ct);
        RequireDeveloper(actor);
        return workspace;
    }

    public async Task<SkillRecoveryHistoryPage> HistoryAsync(string actor, string? employeeCode, int page, CancellationToken ct)
    {
        RequireDeveloper(actor);
        if ((employeeCode != null && !SkillRecoveryRules.ValidCode(employeeCode)) || !ValidPage(page))
            throw new ArgumentException("Choose a valid employee and history page.");
        var history = await _store.HistoryAsync(employeeCode, page, ct);
        RequireDeveloper(actor);
        return history;
    }

    public async Task<SkillRecoveryResult> PreviewAsync(string actor, string sessionId, SkillRecoveryRequest request, CancellationToken ct)
    {
        if (!Authorized(actor)) return Failed("Developer access is required.");
        if (!ValidSession(sessionId)) return Failed("Your session expired. Reload this page.");
        var reasonError = SkillRecoveryRules.ValidateReason(request.Reason);
        if (reasonError != null) return Failed(reasonError);
        var clean = Clean(request);
        var error = SkillRecoveryRules.ValidateRequest(clean);
        if (error != null) return Failed(error);
        try
        {
            var context = await _store.PrepareAsync(actor, clean, ct);
            if (clean.Action == "replace-pdf")
                context = context with { ReplacementPdf = DescribePdfUpload(actor, sessionId, context.Source!.Key, clean.PdfUploadId!.Value) };
            if (!Authorized(actor)) return Failed("Your Developer access changed. No changes were saved.");
            error = SkillRecoveryRules.Validate(clean, context, requireConflictConsent: false, now: _clock.GetLocalNow().DateTime);
            if (error != null) return Failed(error);
            var expiresAt = _clock.GetUtcNow().AddMinutes(10);
            if (context.ReplacementPdf is { } replacement && replacement.ExpiresAt < expiresAt) expiresAt = replacement.ExpiresAt;
            var plan = new SkillRecoveryPlan(Guid.NewGuid(), actor, sessionId, expiresAt, clean, context);
            var outcome = SkillRecoveryRules.Outcome(clean, context, _clock.GetLocalNow().DateTime, actor);
            error = SkillRecoveryRules.Validate(clean, context, now: _clock.GetLocalNow().DateTime);
            if (error != null) return new(false, error, new("", plan, outcome));
            var token = _protector.Protect(JsonSerializer.Serialize(plan));
            if (token.Length > MaximumTokenLength) return Failed("This recovery has too much history to preview safely. Review the selected record.");
            return new(true, "Review the changes before applying them.", new(token, plan, outcome));
        }
        catch (SkillRecoveryConflictException conflict) { return Failed(conflict.Message); }
        catch (UnauthorizedAccessException) { return Failed("Your Developer access changed. No changes were saved."); }
    }

    public async Task<SkillRecoveryPdfUploadInfo> StagePdfAsync(string actor, string sessionId,
        SkillRecoveryKey key, IFormFile upload, CancellationToken ct)
    {
        RequireDeveloper(actor);
        if (!ValidSession(sessionId)) throw new UnauthorizedAccessException("Your session expired. Reload this page.");
        if (!SkillRecoveryRules.ValidCode(key.EmployeeCode) || !Enum.IsDefined(key.Table) || key.RowId == Guid.Empty)
            throw new ArgumentException("Choose an exact skill version.");
        var context = await _store.PrepareAsync(actor, new() { Action = "replace-pdf", EmployeeCode = key.EmployeeCode,
            SourceTable = key.Table, SourceRowId = key.RowId }, ct);
        if (!context.SourceEmployee.Current || context.SourceEmployee.EmployeeCode != key.EmployeeCode ||
            context.Source?.Key != key || string.IsNullOrWhiteSpace(context.Source.ProcessName))
            throw new SkillRecoveryConflictException("Choose an exact skill belonging to a current employee.");
        RequireDeveloper(actor);
        var info = await Uploads().StageAsync(actor, sessionId, key, upload, ct);
        RequireDeveloper(actor);
        return info;
    }

    public SkillRecoveryPdfUploadInfo DescribePdfUpload(string actor, string sessionId, SkillRecoveryKey key, Guid uploadId)
    {
        RequireDeveloper(actor);
        if (!ValidSession(sessionId)) throw new UnauthorizedAccessException("Your session expired. Reload this page.");
        try { return Uploads().Describe(actor, sessionId, key, uploadId); }
        catch (Exception error) when (error is FileNotFoundException or InvalidOperationException or ArgumentException)
        { throw new SkillRecoveryConflictException(error.Message); }
    }

    private SkillRecoveryPdfUploads Uploads() => _uploads ?? throw new SkillRecoveryConflictException("PDF uploads are not available. Reload this page or contact support.");

    public async Task<SkillRecoveryResult> ApplyAsync(string actor, string sessionId, string? token, bool confirmed, CancellationToken ct)
    {
        if (!Authorized(actor)) return Failed("Developer access is required.");
        if (!confirmed) return Failed("Confirm that you reviewed the changes.");
        if (!ValidSession(sessionId) || string.IsNullOrWhiteSpace(token) || token.Length > MaximumTokenLength)
            return Failed("The preview is invalid. Preview the action again.");
        SkillRecoveryPlan? plan;
        try { plan = JsonSerializer.Deserialize<SkillRecoveryPlan>(_protector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { return Failed("The preview is invalid. Preview the action again."); }
        if (plan == null || plan.Actor != actor || plan.SessionId != sessionId || plan.ActionId == Guid.Empty ||
            plan.ExpiresAt <= _clock.GetUtcNow() || plan.ExpiresAt > _clock.GetUtcNow().AddMinutes(10))
            return Failed("The preview expired or belongs to another session. Preview again.");
        var validation = SkillRecoveryRules.Validate(plan.Request, plan.Context, now: _clock.GetLocalNow().DateTime);
        if (validation != null) return Failed(validation);
        try
        {
            var applied = await _store.ApplyAsync(plan, () => Authorized(actor) && plan.ExpiresAt > _clock.GetUtcNow(), ct);
            _logger.LogInformation("Skill recovery {ActionId} applied by {Actor}. Action={Action}; Employee={Employee}; Target={Target}; Row={Row}; Reverses={Reverses}",
                applied.ActionId, actor, plan.Request.Action, plan.Request.EmployeeCode, plan.Request.TargetEmployeeCode,
                plan.Request.SourceRowId, plan.Request.ReversesActionId);
            return new(true, applied.Message, ActionId: applied.ActionId);
        }
        catch (SkillRecoveryConflictException conflict) { return Failed(conflict.Message); }
    }

    private bool Authorized(string actor) => SkillRecoveryRules.ValidCode(actor) && _developers.IsDeveloper(actor);
    private void RequireDeveloper(string actor)
    {
        if (!Authorized(actor)) throw new UnauthorizedAccessException("Developer access is required.");
    }
    private static bool ValidSession(string? session) => !string.IsNullOrWhiteSpace(session) && session.Length <= 256;
    private static bool ValidPage(int page) => page is >= 1 and <= 10000;
    private static SkillRecoveryRequest Clean(SkillRecoveryRequest request)
    {
        var action = request.Action?.Trim() ?? "";
        return request with
        {
            Action = action, EmployeeCode = request.EmployeeCode?.Trim() ?? "", Reason = request.Reason?.Trim() ?? "",
            TargetEmployeeCode = action == "move" ? request.TargetEmployeeCode?.Trim() : null,
            PriorRowId = action == "undo-edit" ? request.PriorRowId : null,
            ReversesActionId = action == "undo" ? request.ReversesActionId : null,
            TargetTab = action == "move-tab" ? request.TargetTab?.Trim().ToLowerInvariant() : null,
            PdfUploadId = action == "replace-pdf" ? request.PdfUploadId : null,
            ReplaceConflict = SkillRecoveryRules.ShouldReplaceConflicts(request with
                { Action = action, TargetTab = request.TargetTab?.Trim().ToLowerInvariant() }) && request.ReplaceConflict
        };
    }
    private static SkillRecoveryResult Failed(string message) => new(false, message);
}
