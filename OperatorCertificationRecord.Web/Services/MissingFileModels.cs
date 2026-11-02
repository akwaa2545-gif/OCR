namespace OperatorCertificationRecord.Web.Services;

public enum MissingFileHealth { Available, Missing, NotLinked, Unavailable }
public sealed record MissingFileQuery(string? Search = null, string Type = "all", int Page = 1, int PageSize = 25);
public sealed record MissingFileCandidate(string EmployeeCode, string EmployeeName, string Kind,
    string? ProcessName, Guid? RowId, Guid? FileId, string? LegacyPath, bool CanRepair,
    long? ByteLength, long? ContentLength, bool SqlFound, bool IsAlias = false);
public sealed record MissingFileCandidates(IReadOnlyList<MissingFileCandidate> Rows, bool HasMore);
public sealed record MissingFileCheck(MissingFileHealth Health, string Detail, string? PreviewUrl = null);
public sealed record MissingFileResult(MissingFileCandidate File, MissingFileHealth Health, string Detail,
    string? PreviewUrl, string RepairUrl, string RepairLabel);
public sealed record MissingFilePage(IReadOnlyList<MissingFileResult> Rows, bool HasMore, MissingFileQuery Query)
{
    public static MissingFilePage Empty { get; } = new([], false, new());
}
public interface IMissingFileStore
{
    Task<MissingFileCandidates> ReadAsync(MissingFileQuery query, CancellationToken cancellationToken);
}
public interface IMissingFileProbe
{
    Task<MissingFileCheck> CheckAsync(MissingFileCandidate candidate, CancellationToken cancellationToken);
}
public interface IMissingFileService
{
    Task<MissingFilePage> ScanAsync(string actor, MissingFileQuery query, CancellationToken cancellationToken);
}
public interface IMissingFileAccess
{
    MissingFileHealth Check(string root, string relativePath, bool requiredRoot, bool photo);
}
