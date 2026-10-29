using System.Data.SqlClient;

namespace OperatorCertificationRecord.FileMigration.Staging;

public static class StageCli
{
    public static async Task<int> ExecuteAsync(string command, Dictionary<string, string> flags, Func<SqlConnection> factory, CancellationToken ct)
    {
        if (flags.ContainsKey("clients-sql-aware")) throw new InvalidOperationException("Staging does not assert or require application deployment.");
        var work = Required(flags, "work-root"); var path = Required(flags, "manifest"); var kind = Required(flags, "employee-kind");
        if (kind is not ("Photo" or "Certificate")) throw new InvalidOperationException("Choose Photo or Certificate staging.");
        var employees = flags.TryGetValue("employees", out var values) ? values.Split(',', StringSplitOptions.RemoveEmptyEntries) : null;
        MigrationJournal.ProtectRoot(work);
        using var runLock = MigrationJournal.Lock(work);
        if (command == "stage-inventory")
        {
            if (flags.ContainsKey("apply")) throw new InvalidOperationException("Inventory cannot apply data.");
            var maps = StagePathMap.Parse(Required(flags, "path-maps"));
            var manifest = await new StageInventory(new SqlInventoryDatabase(factory)).ExecuteAsync(maps, ct, employees, kind);
            await MigrationJournal.SaveManifestAsync(path, manifest, work, ct);
            foreach (var group in manifest.Entries.GroupBy(x => x.Resolution)) Console.WriteLine($"Stage inventory {group.Key}: {group.Count()} files, {group.Sum(x => x.ByteLength)} bytes");
            Console.WriteLine("Staging format 2 saved. Existing application paths and ownership remain unchanged.");
            return 0;
        }
        var input = await MigrationJournal.LoadManifestAsync(path, work, ct);
        var apply = flags.ContainsKey("apply");
        if (apply && (flags.ContainsKey("dry-run") || employees == null))
            throw new InvalidOperationException("Staging apply needs an explicit employee allowlist and cannot combine dry-run.");
        var scope = employees ?? input.Entries.Select(x => x.EmployeeCode).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var options = new StageOptions(apply, flags.GetValueOrDefault("expected-database"), scope, kind,
            Int(flags, "max-files", 10), Long(flags, "max-bytes", 134_217_728), Int(flags, "max-seconds", 45));
        var results = await new StageCommand(new SqlStageStore(factory)).ExecuteAsync(input, options, ct);
        var checkpoint = flags.GetValueOrDefault("checkpoint") ?? Path.Combine(work, "stage-checkpoint-" + MigrationJournal.Hash(input)[..16] + ".json");
        await MigrationJournal.SaveCheckpointAsync(checkpoint, input, results, work, ct);
        foreach (var group in results.GroupBy(x => x.Outcome)) Console.WriteLine($"{group.Key}: {group.Count()}");
        Console.WriteLine("Staged SQL copies are nonauthoritative point-in-time data. No app deployment, owner activation or source deletion occurred.");
        return 0;
    }
    private static string Required(Dictionary<string, string> flags, string name) => flags.GetValueOrDefault(name) is { Length: > 0 } value ? value : throw new InvalidOperationException("Required option: --" + name);
    private static int Int(Dictionary<string, string> flags, string name, int fallback) => flags.TryGetValue(name, out var value) ? int.Parse(value) : fallback;
    private static long Long(Dictionary<string, string> flags, string name, long fallback) => flags.TryGetValue(name, out var value) ? long.Parse(value) : fallback;
}
