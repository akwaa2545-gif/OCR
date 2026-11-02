using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.FileMigration.Staging;

public sealed class StageInventory(IInventoryDatabase database)
{
    public async Task<MigrationManifest> ExecuteAsync(StagePathMap[] maps, CancellationToken ct, string[]? employees = null, string? kind = null)
    {
        if (maps.Length == 0) throw new InvalidOperationException("Explicit logical-prefix path maps are required.");
        foreach (var map in maps)
        {
            if (!Directory.Exists(map.PhysicalRoot)) throw new DirectoryNotFoundException("An approved physical root is inaccessible.");
            SourceFiles.AssertNoReparse(map.PhysicalRoot);
        }
        var snapshot = await database.ReadAsync(ct);
        var byEmployee = snapshot.Employees.ToDictionary(x => x.EmployeeCode, StringComparer.Ordinal);
        var candidates = new List<ManifestEntry>();
        foreach (var owner in snapshot.Owners.Where(x => maps.Any(m => m.Kind == x.Kind) && (kind == null || x.Kind == kind)
                     && (employees == null || employees.Contains(x.EmployeeCode, StringComparer.Ordinal)))
                     .DistinctBy(x => (x.EmployeeCode, x.Kind, x.RelativeKey)))
        {
            ct.ThrowIfCancellationRequested();
            var blank = new ManifestEntry(owner.EmployeeCode, owner.Kind, "", "", 0, "", owner.OwnerTable, null, null, null, false, Resolution.Unsupported, ExpectedLegacyPath: owner.RelativeKey);
            if (!byEmployee.TryGetValue(owner.EmployeeCode, out var employee)) { candidates.Add(blank with { Resolution = Resolution.UnknownEmployee }); continue; }
            if (!CurrentEmployeePolicy.IsCurrent(employee.ResignDate, employee.StatusWork)) { candidates.Add(blank with { Resolution = Resolution.NotCurrent }); continue; }
            if (owner.FileId != null) { candidates.Add(blank with { Reason = "Already SQL-authoritative; no disk staging." }); continue; }
            var found = false;
            foreach (var map in maps.Where(x => x.Kind == owner.Kind))
            {
                try
                {
                    var path = map.Resolve(owner.RelativeKey); if (path == null) continue; found = true;
                    var key = owner.Kind.ToLowerInvariant() + "/" + SourceFiles.CanonicalKey(Path.GetRelativePath(map.PhysicalRoot, path));
                    var candidate = blank with { SourcePath = path, RelativeKey = key };
                    if (!File.Exists(path)) { candidates.Add(candidate with { Resolution = Resolution.Missing, Reason = "Missing or unreadable mapped source." }); continue; }
                    var fingerprint = await SourceFiles.FingerprintAsync(path, owner.Kind, ct);
                    candidates.Add(candidate with { ByteLength = fingerprint.Length, Sha256 = fingerprint.Sha256, Resolution = Resolution.Ready });
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                { candidates.Add(blank with { Reason = "Mapped source is unsafe, unsupported or inaccessible." }); }
            }
            if (!found) candidates.Add(blank with { Reason = "No explicit logical-prefix mapping matched the stored database reference." });
        }
        var outcomes = new List<ManifestEntry>();
        foreach (var group in candidates.GroupBy(x => x.RelativeKey.Length == 0 ? x.EmployeeCode + "\n" + x.Kind + "\n" + x.ExpectedLegacyPath : x.Kind + "\n" + x.RelativeKey, StringComparer.Ordinal))
        {
            var ready = group.Where(x => x.Resolution == Resolution.Ready).DistinctBy(x => (x.EmployeeCode, x.SourcePath, x.Sha256)).ToArray();
            if (group.Select(x => x.EmployeeCode).Distinct(StringComparer.Ordinal).Count() > 1)
                outcomes.AddRange(group.DistinctBy(x => x.EmployeeCode).Select(x => x with { Resolution = Resolution.AmbiguousOwner, Reason = "The same mapped physical key is referenced by different employees." }));
            else if (ready.Select(x => x.Sha256).Distinct(StringComparer.Ordinal).Count() > 1)
                outcomes.Add(group.First() with { Resolution = Resolution.SourceConflict, Reason = "Mapped copies differ; no winner selected." });
            else outcomes.Add(ready.FirstOrDefault() ?? group.First());
        }
        return new(2, snapshot.Database, DateTime.UtcNow, false, maps.Select(x => Path.GetFullPath(x.PhysicalRoot)).Distinct().ToArray(), outcomes.ToArray());
    }
}
