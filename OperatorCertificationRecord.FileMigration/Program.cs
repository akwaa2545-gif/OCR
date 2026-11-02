using System.Data.SqlClient;
using System.Text.Json;

namespace OperatorCertificationRecord.FileMigration;
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help") { Help(); return 0; }
            var flags = Parse(args.Skip(1).ToArray());
            var work = Required(flags, "work-root");
            if (Path.GetFullPath(work).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetTempPath()) + Path.DirectorySeparatorChar, SourceFiles.PathComparison))
                throw new InvalidOperationException("Use a persistent private work root outside the temporary directory.");
            var path = Required(flags, "manifest");
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            var ct = cancellation.Token;
            if (args[0] is "stage-inventory" or "stage")
                return await Staging.StageCli.ExecuteAsync(args[0], flags, Factory(flags), ct);
            if (args[0] == "inventory")
            {
                var roots = Required(flags, "roots").Split(';', StringSplitOptions.RemoveEmptyEntries);
                RetainedOwnership[]? retained = null;
                if (flags.TryGetValue("retained-ownership", out var reviewed))
                {
                    retained = await MigrationJournal.LoadRetainedOwnershipAsync(reviewed, work, ct);
                }
                var result = await new InventoryCommand(new SqlInventoryDatabase(Factory(flags))).ExecuteAsync(roots, ct, retained);
                await MigrationJournal.SaveManifestAsync(path, result, work, ct);
                Console.WriteLine($"Manifest saved: {result.Entries.Length} outcomes. Stable owner keys verified: {result.StableKeysVerified}.");
                PrintTotals(result); return 0;
            }
            var manifest = await MigrationJournal.LoadManifestAsync(path, work, ct);
            using var runLock = MigrationJournal.Lock(work);
            if (args[0] is not ("verify-manifest" or "import")) throw new InvalidOperationException("Unknown command.");
            var kind = Required(flags, "employee-kind");
            var codes = flags.TryGetValue("employees", out var selected) ? selected.Split(',', StringSplitOptions.RemoveEmptyEntries) : manifest.Entries.Select(x => x.EmployeeCode).Where(x => x.Length > 0).Distinct().ToArray();
            var apply = args[0] == "import" && flags.ContainsKey("apply");
            if (apply && flags.ContainsKey("dry-run")) throw new InvalidOperationException("Choose either apply or dry-run.");
            if (apply && !flags.ContainsKey("employees")) throw new InvalidOperationException("Apply requires an explicit employee allowlist.");
            var options = new ImportOptions(apply, flags.GetValueOrDefault("expected-database"), codes, kind, flags.ContainsKey("clients-sql-aware"),
                Number(flags, "max-files", 10), LongNumber(flags, "max-bytes", 134_217_728), Number(flags, "max-seconds", 45),
                ExclusiveMigrationWindow: flags.ContainsKey("exclusive-migration-window"));
            if (apply && options.ExclusiveMigrationWindow)
                Console.WriteLine("Exclusive migration window: the operator must keep old writers unused until upgraded. This option does not deploy apps or verify client compatibility.");
            var store = new SqlImportStore(Factory(flags));
            var outcomes = await new ImportCommand(store).ExecuteAsync(manifest, options, ct);
            var checkpoint = flags.GetValueOrDefault("checkpoint") ?? Path.Combine(work, "checkpoint-" + MigrationJournal.Hash(manifest)[..16] + ".json");
            SourceFiles.AssertUnderRoots(checkpoint, new[] { work });
            await MigrationJournal.SaveCheckpointAsync(checkpoint, manifest, outcomes, work, ct);
            foreach (var group in outcomes.GroupBy(x => x.Outcome)) Console.WriteLine($"{group.Key}: {group.Count()}");
            PrintTotals(manifest);
            return 0;
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("Stopped at the invocation budget or cancellation. Retry the same manifest; committed SQL journal entries are idempotent."); return 3; }
        catch (SqlException) { Console.Error.WriteLine("Database operation failed. No connection details are logged. Review SQL availability and the isolated deployment checks."); return 4; }
        catch (System.Data.DBConcurrencyException) { Console.Error.WriteLine("Attachment owner changed or is already SQL-authoritative. Refresh reconciliation before retrying this file."); return 5; }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidOperationException or FormatException or JsonException or UnauthorizedAccessException)
        { Console.Error.WriteLine(ex is InvalidDataException or InvalidOperationException ? ex.Message : "Invalid arguments or inaccessible migration state/source. No credentials are logged."); return 2; }
    }
    private static Func<SqlConnection> Factory(Dictionary<string,string> flags)
    {
        var value = Environment.GetEnvironmentVariable(flags.GetValueOrDefault("connection-env") ?? "OCR_FILE_MIGRATION_CONNECTION");
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException("Set the private connection environment variable before this command.");
        return () => new SqlConnection(value);
    }
    private static Dictionary<string,string> Parse(string[] args)
    {
        var switches = new[] { "apply", "dry-run", "clients-sql-aware", "exclusive-migration-window" };
        var values = new[] { "work-root", "manifest", "roots", "path-maps", "retained-ownership", "employees", "employee-kind", "expected-database", "checkpoint", "max-files", "max-bytes", "max-seconds", "connection-env" };
        var output = new Dictionary<string,string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new InvalidOperationException("Use named command options.");
            var name = args[i][2..];
            if (!switches.Contains(name) && !values.Contains(name)) throw new InvalidOperationException("Unknown option. Connection strings are never accepted on the command line.");
            if (!output.TryAdd(name, switches.Contains(name) ? "true" : ++i < args.Length ? args[i] : throw new InvalidOperationException("Missing option value."))) throw new InvalidOperationException("Duplicate option.");
        }
        return output;
    }
    private static string Required(Dictionary<string,string> flags, string name) => flags.GetValueOrDefault(name) is { Length: > 0 } value ? value : throw new InvalidOperationException("Required option: --" + name);
    private static int Number(Dictionary<string,string> flags, string name, int fallback) => flags.TryGetValue(name, out var value) ? int.Parse(value) : fallback;
    private static long LongNumber(Dictionary<string,string> flags, string name, long fallback) => flags.TryGetValue(name, out var value) ? long.Parse(value) : fallback;
    private static void PrintTotals(MigrationManifest manifest)
    { foreach (var group in manifest.Entries.GroupBy(x => x.Resolution)) Console.WriteLine($"Manifest {group.Key}: {group.Count()} files, {group.Sum(x => x.ByteLength)} bytes"); }
    private static void Help() => Console.WriteLine("inventory --roots <approved-root;mirror-root> --manifest <new.json> --work-root <private-persistent-dir>\nverify-manifest|import [--dry-run] --manifest <json> --work-root <dir> --employee-kind Photo|Certificate [--employees 00123,00456]\nimport --apply additionally requires --expected-database <database> --employees <allowlist> and either --clients-sql-aware or --exclusive-migration-window\nExclusive migration window is an operator-maintained offline window: old writers must remain unused until upgraded; it does not deploy or verify client compatibility.\nstage-inventory --path-maps <Kind|logical-prefix|physical-root;...> --employee-kind Photo|Certificate --manifest <new.json> --work-root <dir> [--employees <allowlist>]\nstage [--dry-run] --employee-kind Photo|Certificate --manifest <format2.json> --work-root <dir>\nstage --apply additionally requires --expected-database <database> --employees <allowlist>; never activates files or claims deployment\nOptional: --checkpoint <private-path> --max-files 10 --max-bytes 134217728 --max-seconds 45\nCredentials: OCR_FILE_MIGRATION_CONNECTION environment variable only (or --connection-env <variable-name>).");
}
