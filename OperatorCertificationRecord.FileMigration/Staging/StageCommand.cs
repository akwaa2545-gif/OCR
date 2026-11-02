using System.Diagnostics;

namespace OperatorCertificationRecord.FileMigration.Staging;

public sealed record StageOptions(bool Apply, string? ExpectedDatabase, string[] EmployeeCodes, string Kind,
    int MaxFiles = 10, long MaxBytes = 134_217_728, int MaxSeconds = 45);

public interface IStageStore
{
    Task<string> DatabaseNameAsync(CancellationToken ct);
    Task<bool> IsCurrentAsync(string employee, CancellationToken ct);
    Task<Guid?> FindStagedAsync(ManifestEntry entry, CancellationToken ct);
    Task<ImportResult> StageAsync(ManifestEntry entry, Guid manifestId, CancellationToken ct);
}

/// <summary>Format 2 stages immutable copies only; it cannot activate an application attachment.</summary>
public sealed class StageCommand(IStageStore store)
{
    public async Task<ImportResult[]> ExecuteAsync(MigrationManifest manifest, StageOptions options, CancellationToken ct)
    {
        Validate(manifest, options);
        var database = await store.DatabaseNameAsync(ct);
        if (database != manifest.Database || database != (options.ExpectedDatabase ?? manifest.Database))
            throw new InvalidOperationException("Staging database does not match the manifest and explicit target.");
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(TimeSpan.FromSeconds(options.MaxSeconds));
        var clock = Stopwatch.StartNew(); var results = new List<ImportResult>(); long bytes = 0; var files = 0;
        var manifestId = new Guid(Convert.FromHexString(MigrationJournal.Hash(manifest)).AsSpan(0, 16));
        foreach (var entry in manifest.Entries.Where(x => x.Resolution == Resolution.Ready && x.Kind == options.Kind
                     && options.EmployeeCodes.Contains(x.EmployeeCode, StringComparer.Ordinal)))
        {
            if (clock.Elapsed.TotalSeconds >= options.MaxSeconds) break;
            budget.Token.ThrowIfCancellationRequested();
            if (options.Apply && await store.FindStagedAsync(entry, budget.Token) is Guid previous)
            { results.Add(new(entry.EmployeeCode, entry.RelativeKey, "AlreadyStaged", previous)); continue; }
            if (!await store.IsCurrentAsync(entry.EmployeeCode, budget.Token))
            { results.Add(new(entry.EmployeeCode, entry.RelativeKey, "NoLongerCurrent")); continue; }
            if (files == options.MaxFiles || bytes + entry.ByteLength > options.MaxBytes) break;
            SourceFiles.AssertUnderRoots(entry.SourcePath, manifest.ApprovedRoots);
            var validKey = manifest.ApprovedRoots.Any(root => entry.SourcePath.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, SourceFiles.PathComparison)
                && entry.RelativeKey == entry.Kind.ToLowerInvariant() + "/" + SourceFiles.CanonicalKey(Path.GetRelativePath(root, entry.SourcePath)));
            if (!validKey) throw new InvalidDataException("Staging key does not match an approved physical root.");
            var fingerprint = await SourceFiles.FingerprintAsync(entry.SourcePath, entry.Kind, budget.Token);
            if (fingerprint.Length != entry.ByteLength || fingerprint.Sha256 != entry.Sha256)
                throw new InvalidDataException("Source changed since staging inventory; refresh the manifest.");
            files++; bytes += entry.ByteLength;
            results.Add(options.Apply ? await store.StageAsync(entry, manifestId, budget.Token)
                : new(entry.EmployeeCode, entry.RelativeKey, "VerifiedStageDryRun"));
        }
        return results.ToArray();
    }

    public static void Validate(MigrationManifest manifest, StageOptions options)
    {
        if (manifest.FormatVersion != 2 || manifest.StableKeysVerified || options.Kind is not ("Photo" or "Certificate"))
            throw new InvalidOperationException("Use a staging-purpose format 2 manifest and an explicit content kind.");
        if (options.MaxFiles is < 1 or > 10 || options.MaxBytes is < 1 or > 134_217_728 || options.MaxSeconds is < 1 or > 45)
            throw new InvalidOperationException("Staging budgets cannot exceed 10 files / 128 MiB / 45 seconds.");
        if (options.EmployeeCodes.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 10)
            || options.Apply && (string.IsNullOrWhiteSpace(options.ExpectedDatabase) || options.EmployeeCodes.Length == 0))
            throw new InvalidOperationException("Staging apply requires an explicit database and textual employee allowlist.");
        foreach (var e in manifest.Entries.Where(x => x.Resolution == Resolution.Ready))
            if (e.Activate || e.ExpectedFileId != null || e.ExpectedVersion != null || e.OwnerKey != null
                || e.Kind is not ("Photo" or "Certificate") || e.ByteLength <= 0 || e.Sha256.Length != 64
                || e.EmployeeCode.Length is < 1 or > 10 || string.IsNullOrWhiteSpace(e.ExpectedLegacyPath)
                || e.OwnerTable is not ("Employee" or "Qualified" or "Obsolete"))
                throw new InvalidDataException("Staging entries require exact legacy provenance and cannot contain active owner bindings.");
    }
}
