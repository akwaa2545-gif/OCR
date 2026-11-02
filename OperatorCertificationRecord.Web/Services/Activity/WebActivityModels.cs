namespace OperatorCertificationRecord.Web.Services;

public sealed record WebActivityActor(string ActorCode, string? ActorName, bool? IsAdmin,
    bool? IsDeveloper, Guid OperationId, string RequestId, string Route);

public sealed record WebActivityScope(string Kind, string EmployeeCode, SkillRecoveryTable? Table = null,
    Guid? RowId = null, string? ProcessName = null)
{
    public static WebActivityScope Employee(string code) => new("employee", code);
    public static WebActivityScope EmployeeAndSkills(string code) => new("employee-skills", code);
    public static WebActivityScope Skills(string code, string? processName = null) => new("skills", code, ProcessName: processName);
    public static WebActivityScope Skill(SkillRecoveryKey key) => new("skill", key.EmployeeCode, key.Table, key.RowId);
}

public sealed record WebActivityEntitySnapshot(string EntityType, string EntityKey, string? EmployeeCode,
    string? SkillName, string? TableKind, string Label, IReadOnlyDictionary<string, string?> Fields);

public sealed record WebActivityFieldChange(string Field, string? Before, string? After, bool Redacted = false);
public sealed record WebActivityChange(string EntityType, string EntityKey, string? EmployeeCode,
    string? SkillName, string? TableKind, string Label, bool BeforeExists, bool AfterExists,
    IReadOnlyList<WebActivityFieldChange> Fields);

public sealed record WebActivityRecord(Guid EventUid, WebActivityActor Actor, string Category,
    string Action, string Outcome, string Summary, IReadOnlyList<WebActivityChange> Changes,
    DateTimeOffset OccurredUtc, string OriginKind = "WebCommand", Guid? OriginId = null,
    string Part = "0", Guid? RecoveryActionId = null, string? CommandHash = null, string? RequestHash = null);

public sealed record WebActivityCapture(WebActivityActor Actor, string Category, string Action,
    IReadOnlyList<WebActivityScope> Scopes, IReadOnlyList<WebActivityEntitySnapshot> Before, string Part,
    string OriginKind = "WebCommand", Guid? OriginId = null, bool Replayed = false,
    long? ExistingEventId = null, int ExistingChangesCount = 0, string? CommandHash = null, string? RequestHash = null);

public sealed record WebActivityQuery
{
    public string Tab { get; init; } = "changes";
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public string? Actor { get; init; }
    public string? Employee { get; init; }
    public string? Skill { get; init; }
    public string? Action { get; init; }
    public string? Outcome { get; init; }
    public int Page { get; init; } = 1;
}

public sealed record WebActivityListRow(long EventId, Guid EventUid, Guid OperationId, DateTime OccurredUtc,
    string ActorCode, string? ActorName, bool? ActorIsAdmin, bool? ActorIsDeveloper,
    string Category, string Action, string Outcome, string Summary, string? EmployeeCode,
    string? SkillName, string? EntityLabel, int ItemCount, Guid? RecoveryActionId);
public sealed record WebActivityPage(IReadOnlyList<WebActivityListRow> Rows, int Page, bool HasMore,
    IReadOnlyList<string> Actions);
public sealed record WebActivityDetail(WebActivityListRow Event, string RequestId, string Route,
    string OriginKind, IReadOnlyList<WebActivityChange> Changes, string? RecoveryEmployeeCode = null);

public interface IWebActivityReader
{
    Task<WebActivityPage> ListAsync(string actor, WebActivityQuery query, CancellationToken ct = default);
    Task<WebActivityDetail?> DetailAsync(string actor, long eventId, CancellationToken ct = default);
}

public static class WebActivityLabels
{
    public static string Action(string action) => action switch
    {
        "employee-add" => "Add employee", "employee-update" => "Update employee",
        "employee-transfer" => "Transfer", "employee-resign" => "Resign", "employee-promote" => "Promote",
        "skill-add" => "Add skill", "skill-update" => "Update skill", "skill-disqualify" => "Disqualify skill",
        "photo-change" => "Change photo", "password-change" => "Change password",
        "workshop-add" => "Add workshop", "process-add" => "Add process", "process-move" => "Move process",
        "bulk-update" or "employee-bulk-update" => "Bulk update", "admin-enable" => "Give Admin", "admin-remove" => "Remove Admin",
        "developer-enable" => "Give Developer", "developer-remove" => "Remove Developer",
        "page-view" => "Open page", "report-ready" => "Prepare report", "preview" => "Preview",
        "login" => "Sign in", "logout" => "Sign out",
        _ when SkillRecoveryRules.Label(action) != "Recovery" => SkillRecoveryRules.Label(action),
        _ => action.Replace('-', ' ')
    };
    public static string Outcome(string outcome) => outcome switch
    {
        "Committed" => "Saved", "Denied" => "Blocked", "Prepared" => "Ready",
        "NoChange" => "No change", "Viewed" => "Viewed", "Succeeded" => "Success", "Rejected" => "Rejected",
        "Failed" => "Failed", "Partial" => "Partial", _ => "Unknown"
    };

    public static string Field(string field) => field switch
    {
        "EmpCode" => "Employee ID", "JobGrade" => "Job grade", "DeptID" => "Department",
        "SectID" => "Section", "WorkshopID" => "Workshop", "JoinDate" => "Join date",
        "PersonFnameEng" => "First name (English)", "PersonLnameEng" => "Last name (English)",
        "PersonFnameThai" => "First name (Thai)", "PersonLnameThai" => "Last name (Thai)",
        "HEng" => "Prefix (English)", "HThai" => "Prefix (Thai)", "ProcessName" => "Skill / process",
        "OperatorTraining" => "Classification", "CertificateFileId" => "PDF version",
        "PhotoFileId" => "Photo version", "PasswordChanged" => "Password changed",
        "DisQualifiedBy" => "Disqualified by", "TheReason" => "Disqualification reason",
        "CertifiedDate" => "Certified date", "ExpiryDate" => "Expiry date", "OJTTraining" => "OJT training",
        "TheoryTraining" => "Theory training", "PhotoSha256" => "Photo hash", "PdfSha256" => "PDF hash",
        "PdfFileName" => "PDF filename", "PhotoFileName" => "Photo filename", "PdfByteLength" => "PDF size",
        "PhotoByteLength" => "Photo size", "StatusWork" => "Employment status",
        _ => System.Text.RegularExpressions.Regex.Replace(field, "([a-z])([A-Z])", "$1 $2")
    };
}
