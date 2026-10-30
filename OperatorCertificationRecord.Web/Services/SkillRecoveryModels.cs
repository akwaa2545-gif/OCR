namespace OperatorCertificationRecord.Web.Services;

public enum SkillRecoveryTable { Current, Archive }
public sealed record SkillRecoveryKey(string EmployeeCode, SkillRecoveryTable Table, Guid RowId);
public sealed record SkillRecoveryEmployee(string EmployeeCode, string Name, bool Current,
    string? JobGrade = null, string? StatusWork = null, DateTime? ResignDate = null);

public sealed record SkillRecoveryValues
{
    public string ProcessName { get; init; } = "";
    public string? OperatorTraining { get; init; }
    public DateTime? TheoryTraining { get; init; }
    public DateTime? OJTTraining { get; init; }
    public DateTime? CertifiedDate { get; init; }
    public string? FullScore { get; init; }
    public string? ActualScore { get; init; }
    public string? TestResult { get; init; }
    public string? JudgmentTheory { get; init; }
    public string? KnowledgeScore { get; init; }
    public string? KnowledgeLevel { get; init; }
    public string? SkillScore { get; init; }
    public string? SkillLevel { get; init; }
    public string? JudgmentPractice { get; init; }
    public DateTime? ExpiryDate { get; init; }
    public string? Verifier { get; init; }
    public DateTime? VerifierDate { get; init; }
    public DateTime? DisQualifiedDate { get; init; }
    public string? DisQualifiedBy { get; init; }
    public string? TheReason { get; init; }
    public string? Remark { get; init; }
    public string? Download { get; init; }
    public Guid? PromotionId { get; init; }
    public DateTime? PromotedAt { get; init; }
    public string? PromotedBy { get; init; }
    public Guid? CertificateFileId { get; init; }
}

public sealed record SkillRecoveryRow(SkillRecoveryTable Table, string EmployeeCode, Guid RowId,
    string Version, SkillRecoveryValues Values)
{
    public SkillRecoveryKey Key => new(EmployeeCode, Table, RowId);
    public string ProcessName => Values.ProcessName;
}

public sealed record SkillRecoveryRequest
{
    public string Action { get; init; } = "reinstate";
    public string EmployeeCode { get; init; } = "";
    public SkillRecoveryTable SourceTable { get; init; }
    public Guid SourceRowId { get; init; }
    public string? TargetEmployeeCode { get; init; }
    public Guid? PriorRowId { get; init; }
    public bool ReplaceConflict { get; init; }
    public string? Reason { get; init; } = "";
    public Guid? ReversesActionId { get; init; }
    public string? TargetTab { get; init; }
    public Guid? PdfUploadId { get; init; }
}

// All rows are retrieved by the store, never accepted as client-provided snapshots.
// UndoPayload is opaque store-produced audit state, encrypted in the protected preview.
public sealed record SkillRecoveryContext(SkillRecoveryEmployee SourceEmployee, SkillRecoveryRow? Source,
    SkillRecoveryEmployee? TargetEmployee, SkillRecoveryRow? Prior,
    IReadOnlyList<SkillRecoveryRow> Conflicts, string? UndoPayload = null,
    bool UndoAllowed = false, string? UndoUnavailableReason = null, string? SourceFileHash = null,
    IReadOnlyList<SkillRecoveryRow>? UndoBeforeRows = null, IReadOnlyList<SkillRecoveryRow>? UndoAfterRows = null,
    string? OriginalAction = null, SkillRecoveryPdfUploadInfo? ReplacementPdf = null);
public sealed record SkillRecoveryArchiveCopy(SkillRecoveryKey Source, Guid ArchiveRowId);
public sealed record SkillRecoveryPlan(Guid ActionId, string Actor, string SessionId, DateTimeOffset ExpiresAt,
    SkillRecoveryRequest Request, SkillRecoveryContext Context,
    IReadOnlyList<SkillRecoveryArchiveCopy>? ArchiveCopies = null);
public sealed record SkillRecoveryPreview(string Token, SkillRecoveryPlan Plan, string Outcome);
public sealed record SkillRecoveryResult(bool Success, string Message, SkillRecoveryPreview? Preview = null, Guid? ActionId = null);
public sealed record SkillRecoveryApplyResult(Guid ActionId, string Message);
public sealed record SkillRecoverySearch(IReadOnlyList<SkillRecoveryEmployee> Rows, int Page, bool HasMore);
public sealed record SkillRecoveryWorkspace(SkillRecoveryEmployee? Employee, IReadOnlyList<SkillRecoveryRow> Rows,
    bool HasMore = false);
public sealed record SkillRecoveryHistory(Guid ActionId, string Action, string Actor, DateTime CreatedUtc,
    string Reason, string SourceEmployeeCode, string? TargetEmployeeCode, string ProcessName,
    Guid? ReversesActionId = null, bool CanUndo = true);
public sealed record SkillRecoveryHistoryPage(IReadOnlyList<SkillRecoveryHistory> Rows, int Page, bool HasMore);

public interface ISkillRecoveryStore
{
    Task<SkillRecoverySearch> SearchEmployeesAsync(string query, int page, CancellationToken ct);
    Task<SkillRecoveryWorkspace> ReadWorkspaceAsync(string employeeCode, CancellationToken ct);
    Task<SkillRecoveryContext> PrepareAsync(string actor, SkillRecoveryRequest request, CancellationToken ct);
    Task<SkillRecoveryApplyResult> ApplyAsync(SkillRecoveryPlan plan, Func<bool> authorized, CancellationToken ct);
    Task<SkillRecoveryHistoryPage> HistoryAsync(string? employeeCode, int page, CancellationToken ct);
}

public sealed class SkillRecoveryConflictException(string message) : InvalidOperationException(message);
