namespace OperatorCertificationRecord.FileMigration;

public sealed class InventoryCommand(IInventoryDatabase database)
{
    public static ManifestEntry[] Reconcile(IEnumerable<ManifestEntry> source)
    {
        var output = new List<ManifestEntry>();
        foreach (var group in source.GroupBy(x => (x.EmployeeCode, x.Kind, Key: SourceFiles.CanonicalKey(x.RelativeKey))))
        {
            var rows = group.ToArray();
            var eligible = rows.Where(x => x.Resolution is Resolution.Ready or Resolution.RetainedVersion).ToArray();
            if (eligible.Select(x => (x.OwnerTable, x.OwnerKey)).Distinct().Count() > 1)
                output.AddRange(rows.Select(x => x with { Activate = false, Resolution = Resolution.AmbiguousOwner, Reason = "Relative path maps to multiple business versions." }));
            else if (eligible.Select(x => x.Sha256).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                output.AddRange(rows.Select(x => x with { Activate = false, Resolution = Resolution.SourceConflict, Reason = "Sources have different bytes; explicit reconciliation required." }));
            else
            {
                output.AddRange(rows.Where(x => x.Resolution is not (Resolution.Ready or Resolution.RetainedVersion)));
                if (eligible.Length > 0) output.Add(eligible[0]);
            }
        }
        return output.ToArray();
    }

    public async Task<MigrationManifest> ExecuteAsync(string[] roots, CancellationToken ct, RetainedOwnership[]? retainedOwnership = null)
    {
        var snapshot = await database.ReadAsync(ct);
        var normalizedOwners = snapshot.Owners.Select(x => (Owner: x, Key: NormalizeOwnerKey(x.RelativeKey, roots))).ToArray();
        var ownersByKey = normalizedOwners.Where(x => x.Key != null).ToLookup(x => x.Key!, x => x.Owner, StringComparer.Ordinal);
        ILookup<string, RetainedOwnership>? retainedByKey = null;
        var entries = new List<ManifestEntry>();
        var knownEmployees = snapshot.Employees.Select(x => x.EmployeeCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = snapshot.Employees.Where(x => OperatorCertificationRecord.FileStorage.CurrentEmployeePolicy.IsCurrent(x.ResignDate, x.StatusWork))
            .ToDictionary(x => x.EmployeeCode, StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        foreach (var file in SourceFiles.EnumerateFileInfos(root))
        {
            ct.ThrowIfCancellationRequested();
            var path = file.FullName;
            var relative = SourceFiles.CanonicalKey(Path.GetRelativePath(root, path));
            var owners = ownersByKey[relative].ToArray();
            if (owners.Length == 0)
            {
                retainedByKey ??= (retainedOwnership ?? Array.Empty<RetainedOwnership>()).ToLookup(x => SourceFiles.CanonicalKey(x.RelativeKey), StringComparer.Ordinal);
                var retained = retainedByKey[relative].ToArray();
                if (retained.Length > 0)
                {
                    foreach (var approved in retained)
                    {
                        var resolution = retained.Length > 1 ? Resolution.AmbiguousOwner : !current.ContainsKey(approved.EmployeeCode) ? Resolution.NotCurrent : Resolution.RetainedVersion;
                        var row = new ManifestEntry(approved.EmployeeCode, approved.Kind, relative, path, file.Length, "", null, null, null, null, false, resolution, "Explicit reviewed ownership; no business attachment is created.");
                        if (resolution == Resolution.RetainedVersion)
                        {
                            try { var fp = await SourceFiles.FingerprintAsync(path, approved.Kind, ct); row = row with { ByteLength = fp.Length, Sha256 = fp.Sha256 }; }
                            catch (InvalidDataException ex) { row = row with { Resolution = Resolution.Unsupported, Reason = ex.Message }; }
                        }
                        entries.Add(row);
                    }
                    continue;
                }
                // Ownership must come from database records, never a guessed filename prefix.
                entries.Add(new("", "Unknown", relative, path, file.Length, "", null, null, null, null, false, Resolution.UnknownEmployee, "No database owner."));
                continue;
            }
            foreach (var owner in owners)
            {
                var resolution = !knownEmployees.Contains(owner.EmployeeCode) ? Resolution.UnknownEmployee
                    : !current.ContainsKey(owner.EmployeeCode) ? Resolution.NotCurrent
                    : owners.Length > 1 ? Resolution.AmbiguousOwner
                    : owner.OwnerTable != "Employee" && (!snapshot.StableKeysVerified || owner.OwnerKey == null) ? Resolution.PendingIdentity : Resolution.Ready;
                var row = new ManifestEntry(owner.EmployeeCode, owner.Kind, relative, path, file.Length, "", owner.OwnerTable, owner.OwnerKey, owner.Version, owner.FileId, resolution == Resolution.Ready, resolution, ExpectedLegacyPath: owner.RelativeKey);
                if (resolution == Resolution.Ready)
                {
                    try { var fingerprint = await SourceFiles.FingerprintAsync(path, owner.Kind, ct); row = row with { ByteLength = fingerprint.Length, Sha256 = fingerprint.Sha256 }; }
                    catch (InvalidDataException ex) { row = row with { Activate = false, Resolution = Resolution.Unsupported, Reason = ex.Message }; }
                }
                entries.Add(row);
            }
        }
        var observed = entries.Select(x => (x.EmployeeCode, x.OwnerTable, x.OwnerKey, RelativeKey: (string?)x.RelativeKey)).ToHashSet();
        foreach (var item in normalizedOwners.Where(x => current.ContainsKey(x.Owner.EmployeeCode) || !knownEmployees.Contains(x.Owner.EmployeeCode)))
        {
            var owner = item.Owner;
            if (observed.Contains((owner.EmployeeCode, owner.OwnerTable, owner.OwnerKey, item.Key))) continue;
            var key = item.Key ?? "unresolved-reference";
            entries.Add(new(owner.EmployeeCode, owner.Kind, key, "", 0, "", owner.OwnerTable, owner.OwnerKey, owner.Version, owner.FileId, false, knownEmployees.Contains(owner.EmployeeCode) ? Resolution.Missing : Resolution.UnknownEmployee, ExpectedLegacyPath: owner.RelativeKey));
            observed.Add((owner.EmployeeCode, owner.OwnerTable, owner.OwnerKey, key));
        }
        return new(1, snapshot.Database, DateTime.UtcNow, snapshot.StableKeysVerified, roots.Select(Path.GetFullPath).ToArray(), Reconcile(entries));
    }

    private static string? NormalizeOwnerKey(string legacy, string[] roots)
    {
        try
        {
            if (!Path.IsPathRooted(legacy)) return SourceFiles.CanonicalKey(legacy.TrimStart('/', '\\'));
            foreach (var root in roots)
            {
                var full = Path.GetFullPath(legacy); var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                if (full.StartsWith(prefix, SourceFiles.PathComparison)) return SourceFiles.CanonicalKey(Path.GetRelativePath(root, full));
            }
        }
        catch (ArgumentException) { } catch (InvalidDataException) { }
        return null;
    }
}
