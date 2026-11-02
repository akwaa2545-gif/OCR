using System.Diagnostics;

namespace OperatorCertificationRecord.FileMigration;

public sealed record ImportOptions(bool Apply, string? ExpectedDatabase, string[] EmployeeCodes, string Kind,
    bool ClientsSqlAware, int MaxFiles = 10, long MaxBytes = 134_217_728, int MaxSeconds = 45,
    bool ExclusiveMigrationWindow = false);
public sealed record ImportResult(string EmployeeCode, string RelativeKey, string Outcome, Guid? FileId = null);
public interface IImportStore
{
    Task<string> DatabaseNameAsync(CancellationToken ct);
    Task<bool> IsCurrentAsync(string employee, CancellationToken ct);
    Task<Guid?> FindCommittedAsync(ManifestEntry entry, CancellationToken ct);
    // Implementations recheck current status, owner identity/version/pointer and idempotency in ONE transaction.
    Task<ImportResult> PublishAsync(ManifestEntry entry, CancellationToken ct);
}

public sealed class ImportCommand(IImportStore store)
{
    public async Task<ImportResult[]> ExecuteAsync(MigrationManifest manifest, ImportOptions options, CancellationToken ct)
    {
        Validate(manifest, options);
        if (!string.Equals(await store.DatabaseNameAsync(ct), options.ExpectedDatabase ?? manifest.Database, StringComparison.Ordinal)
            || options.Apply && options.ExpectedDatabase != manifest.Database)
            throw new InvalidOperationException("Expected database does not match the live connection and manifest.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(options.MaxSeconds));
        var clock = Stopwatch.StartNew();
        var results = new List<ImportResult>(); long bytes = 0; int files = 0;
        foreach (var entry in manifest.Entries.Where(x => (x.Resolution is Resolution.Ready or Resolution.RetainedVersion)
                     && x.Kind == options.Kind && options.EmployeeCodes.Contains(x.EmployeeCode, StringComparer.Ordinal)))
        {
            if (clock.Elapsed.TotalSeconds >= options.MaxSeconds) break;
            budget.Token.ThrowIfCancellationRequested();
            if (options.Apply && await store.FindCommittedAsync(entry, budget.Token) is Guid committed)
            { results.Add(new(entry.EmployeeCode, entry.RelativeKey, "AlreadyCommitted", committed)); continue; }
            if (!await store.IsCurrentAsync(entry.EmployeeCode, budget.Token))
            { results.Add(new(entry.EmployeeCode, entry.RelativeKey, "NoLongerCurrent")); continue; }
            if (files == options.MaxFiles || bytes + entry.ByteLength > options.MaxBytes) break;
            SourceFiles.AssertUnderRoots(entry.SourcePath, manifest.ApprovedRoots);
            if (entry.RelativeKey != SourceFiles.CanonicalKey(entry.RelativeKey)) throw new InvalidDataException("Manifest source key is not canonical.");
            if (entry.Activate && (entry.OwnerTable == null || entry.ExpectedVersion == null || entry.OwnerTable != "Employee" && !Guid.TryParse(entry.OwnerKey, out _)))
                throw new InvalidDataException("Active attachment lacks a stable owner identity or concurrency token.");
            var fingerprint = await SourceFiles.FingerprintAsync(entry.SourcePath, entry.Kind, budget.Token);
            if (fingerprint.Length != entry.ByteLength || !string.Equals(fingerprint.Sha256, entry.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("Source changed since the manifest; refresh inventory before importing.");
            files++; bytes += entry.ByteLength;
            results.Add(options.Apply ? await store.PublishAsync(entry, budget.Token)
                : new(entry.EmployeeCode, entry.RelativeKey, "VerifiedDryRun"));
        }
        return results.ToArray();
    }

    public static void Validate(MigrationManifest manifest, ImportOptions options)
    {
        if (manifest.FormatVersion != 1 || options.Kind is not ("Photo" or "Certificate")) throw new InvalidOperationException("Unsupported manifest or content kind.");
        if (options.MaxFiles is < 1 or > 10 || options.MaxBytes is < 1 or > 134_217_728 || options.MaxSeconds is < 1 or > 45)
            throw new InvalidOperationException("Budgets must be positive and cannot exceed 10 files / 128 MiB / 45 seconds.");
        if (options.EmployeeCodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 10)) throw new InvalidOperationException("Invalid employee allowlist.");
        if (options.Apply && ((!options.ClientsSqlAware && !options.ExclusiveMigrationWindow) || !manifest.StableKeysVerified || string.IsNullOrWhiteSpace(options.ExpectedDatabase) || options.EmployeeCodes.Length == 0))
            throw new InvalidOperationException("Apply requires expected database, explicit employee allowlist, stable keys and either verified SQL-aware clients or an operator-maintained exclusive migration window. Old writers must remain unused until upgraded.");
    }
}
