namespace OperatorCertificationRecord.Web.Services;

public sealed record BulkEmployeeRow(string EmpCode, string Name, string? WorkshopId, string? WorkshopName,
    string? Shift, string? JobGrade, bool Promoted, bool Current, string? StatusWork = null, DateTime? ResignDate = null);
public sealed record BulkEmployeeChoice(string Id, string Name);
public sealed record BulkEmployeeChoices(IReadOnlyList<BulkEmployeeChoice> Workshops, IReadOnlyList<BulkEmployeeChoice> Grades)
{
    public static BulkEmployeeChoices Empty { get; } = new([], []);
}
public sealed record BulkEmployeeSearch(IReadOnlyList<BulkEmployeeRow> Rows, int Total, int Page);
public sealed record BulkEmployeePatch(string? WorkshopId = null, string? Shift = null, string? JobGrade = null)
{
    public bool HasChanges => WorkshopId != null || Shift != null || JobGrade != null;
}
public sealed record BulkEmployeeBatch(string Actor, string SessionId, DateTimeOffset ExpiresAt,
    BulkEmployeePatch Patch, IReadOnlyList<BulkEmployeeRow> Rows, string WorkshopName, Guid? OperationId = null);
public sealed record BulkEmployeePreview(string Token, BulkEmployeeBatch Batch);
public sealed record BulkEmployeeResult(bool Success, string Message, BulkEmployeePreview? Preview = null);

public interface IBulkEmployeeStore
{
    Task<BulkEmployeeSearch> SearchAsync(string query, int page, CancellationToken ct);
    Task<BulkEmployeeChoices> ChoicesAsync(CancellationToken ct);
    Task<IReadOnlyList<BulkEmployeeRow>> ReadSelectedAsync(IReadOnlyList<string> codes, CancellationToken ct);
    Task<int> ApplyAsync(BulkEmployeeBatch batch, Func<bool> authorized, CancellationToken ct);
}

public sealed class BulkEmployeeConflictException(string message) : InvalidOperationException(message);
