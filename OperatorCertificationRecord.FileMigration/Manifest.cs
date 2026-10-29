namespace OperatorCertificationRecord.FileMigration;

public enum Resolution { Ready, RetainedVersion, Missing, Unsupported, SourceConflict, AmbiguousOwner, PendingIdentity, UnknownEmployee, NotCurrent }
public sealed record ManifestEntry(string EmployeeCode, string Kind, string RelativeKey, string SourcePath,
    long ByteLength, string Sha256, string? OwnerTable, string? OwnerKey, string? ExpectedVersion,
    Guid? ExpectedFileId, bool Activate, Resolution Resolution, string? Reason = null, string? ExpectedLegacyPath = null);
public sealed record MigrationManifest(int FormatVersion, string Database, DateTime CreatedUtc,
    bool StableKeysVerified, string[] ApprovedRoots, ManifestEntry[] Entries);
public sealed record OwnerSnapshot(string EmployeeCode, string Kind, string RelativeKey, string OwnerTable,
    string? OwnerKey, string? Version, Guid? FileId);
public sealed record EmployeeSnapshot(string EmployeeCode, DateTime? ResignDate, string? StatusWork);
public sealed record RetainedOwnership(string EmployeeCode, string Kind, string RelativeKey);

public interface IInventoryDatabase
{
    Task<(string Database, bool StableKeysVerified, IReadOnlyList<EmployeeSnapshot> Employees, IReadOnlyList<OwnerSnapshot> Owners)> ReadAsync(CancellationToken ct);
}
