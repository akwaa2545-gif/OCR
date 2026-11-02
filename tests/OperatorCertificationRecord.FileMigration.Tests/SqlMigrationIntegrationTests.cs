using System.Data;
using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using OperatorCertificationRecord.FileMigration;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public sealed class SqlMigrationIntegrationTests
{
    private static string ConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION")
                ?? throw new InvalidOperationException("Set the private isolated fixture connection environment variable.");
            var builder = new SqlConnectionStringBuilder(value);
            if (!Regex.IsMatch(builder.InitialCatalog, "^OcrFileStorageTests_[A-Za-z0-9_]+$")
                || !Regex.IsMatch(builder.DataSource, @"^(localhost|127\.0\.0\.1|\(local\)|\.)(,\d+|\\[A-Za-z0-9_]+)?$"))
                throw new InvalidOperationException("Migration integration permits only a dedicated local OcrFileStorageTests_ database.");
            return value;
        }
    }

    [Fact, Trait("Category", "SqlIntegration")]
    public async Task InventoryApplyRetryCasResignationAndRetainedVersionsRemainAtomic()
    {
        var connection = ConnectionString;
        Func<SqlConnection> factory = () => new(connection);
        var repository = new SqlEmployeeFileRepository(factory);
        Assert.True(await repository.IsSchemaAvailableAsync(default));
        var codes = Enumerable.Range(0, 5).Select(_ => "0" + Guid.NewGuid().ToString("N")[..8]).ToArray();
        var skill = Guid.NewGuid(); var orphan = Guid.NewGuid();
        var root = Path.Combine(AppContext.BaseDirectory, "ocr-sql-migration-fixture-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var photos = codes.ToDictionary(x => x, x => x + ".gif");
        var certificate = codes[0] + ".pdf"; var retained = codes[0] + "-old.pdf"; var orphanPath = "orphan-" + Guid.NewGuid().ToString("N") + ".pdf";
        var photoBytes = Encoding.ASCII.GetBytes("GIF89a-generated-synthetic-fixture");
        try
        {
            foreach (var code in codes)
            {
                await File.WriteAllBytesAsync(Path.Combine(root, photos[code]), photoBytes);
                await ExecuteAsync("INSERT dbo.tblEmployee(EmpCode,Photo,StatusWork,ResignDate) VALUES(@employee,@value,N'1',NULL)", code, photos[code]);
            }
            foreach (var name in new[] { certificate, retained, orphanPath }) await File.WriteAllTextAsync(Path.Combine(root, name), "%PDF-1.4\ngenerated synthetic fixture " + name);
            await ExecuteAsync("INSERT dbo.tblQualified(EmpCode,Download,OcrSkillRowId) VALUES(@employee,@value,@id)", codes[0], certificate, skill);
            await ExecuteAsync("INSERT dbo.tblQualified(EmpCode,Download,OcrSkillRowId) VALUES(NULL,@value,@id)", null, orphanPath, orphan);
            await ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=GETDATE() WHERE EmpCode=@employee", codes[3]);
            var work = Path.Combine(root, "private-state"); var manifestPath = Path.Combine(work, "inventory.json");
            MigrationJournal.ProtectRoot(work);
            var ownershipPath = Path.Combine(work, "reviewed-retained.json");
            await File.WriteAllTextAsync(ownershipPath, JsonSerializer.Serialize(new[] { new RetainedOwnership(codes[0], "Certificate", retained) }));
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(ownershipPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            Assert.Equal(0, await Program.Main(new[] { "inventory", "--roots", root, "--work-root", work, "--manifest", manifestPath,
                "--retained-ownership", ownershipPath, "--connection-env", "OCR_FILE_STORAGE_TEST_CONNECTION" }));
            var manifest = await MigrationJournal.LoadManifestAsync(manifestPath, work, default);
            Assert.True(manifest.StableKeysVerified);
            Assert.Equal(Resolution.UnknownEmployee, Assert.Single(manifest.Entries.Where(x => x.RelativeKey == orphanPath)).Resolution);
            Assert.Equal(Resolution.NotCurrent, Assert.Single(manifest.Entries.Where(x => x.EmployeeCode == codes[3])).Resolution);
            var database = new SqlConnectionStringBuilder(connection).InitialCatalog;
            ImportOptions Options(string code, string kind = "Photo", bool apply = true) => new(apply, database, new[] { code }, kind, true);
            ImportCommand Importer() => new(new SqlImportStore(factory));
            string[] Cli(params string[] options) => new[] { "--manifest", manifestPath, "--work-root", work, "--employee-kind", "Photo", "--employees", codes[0],
                "--connection-env", "OCR_FILE_STORAGE_TEST_CONNECTION" }.Concat(options).ToArray();
            Assert.Equal(0, await Program.Main(new[] { "verify-manifest" }.Concat(Cli()).ToArray()));
            Assert.Equal(0, await Program.Main(new[] { "import" }.Concat(Cli("--dry-run")).ToArray()));

            Assert.Equal("VerifiedDryRun", Assert.Single(await Importer().ExecuteAsync(manifest, Options(codes[0], apply: false), default)).Outcome);
            Assert.Equal(0, await CountAsync("dbo.tblEmployeeFile", codes[0]));
            await ExecuteAsync("UPDATE dbo.tblEmployee SET Photo=Photo WHERE EmpCode=@employee", codes[1]);
            await Assert.ThrowsAsync<DBConcurrencyException>(() => Importer().ExecuteAsync(manifest, Options(codes[1]), default));
            Assert.Equal(0, await CountAsync("dbo.tblEmployeeFile", codes[1]));
            Assert.Equal(0, await CountAsync("dbo.tblEmployeeFileMigration", codes[1]));
            await ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=GETDATE() WHERE EmpCode=@employee", codes[2]);
            Assert.Equal("NoLongerCurrent", Assert.Single(await Importer().ExecuteAsync(manifest, Options(codes[2]), default)).Outcome);

            var applied = Assert.Single(await Importer().ExecuteAsync(manifest, Options(codes[0]), default));
            Assert.Equal("Committed", applied.Outcome);
            var retry = Assert.Single(await Importer().ExecuteAsync(manifest, Options(codes[0]), default));
            Assert.Equal("AlreadyCommitted", retry.Outcome); Assert.Equal(applied.FileId, retry.FileId);
            Assert.Equal(0, await Program.Main(new[] { "import" }.Concat(Cli("--apply", "--expected-database", database, "--clients-sql-aware",
                "--checkpoint", Path.Combine(work, "cli-checkpoint.json"), "--max-files", "1", "--max-bytes", "1048576", "--max-seconds", "15")).ToArray()));
            Assert.Equal(1, await CountAsync("dbo.tblEmployeeFile", codes[0])); Assert.Equal(1, await CountAsync("dbo.tblEmployeeFileMigration", codes[0]));
            var owner = await repository.GetOwnerStateAsync(new(FileOwnerTable.Employee, codes[0]), default);
            Assert.Equal(applied.FileId, owner.FileId); Assert.Equal(photos[codes[0]], owner.LegacyPath);
            using (var lease = await repository.OpenReadAsync(applied.FileId!.Value, codes[0], FileKind.Photo, default))
                Assert.Equal(SHA256.HashData(photoBytes), await SHA256.HashDataAsync(lease.Content));

            var certificates = await Importer().ExecuteAsync(manifest, Options(codes[0], "Certificate"), default);
            Assert.Contains(certificates, x => x.Outcome == "Committed"); Assert.Contains(certificates, x => x.Outcome == "RetainedUnlinked");
            Assert.Equal(1, await CountAsync("dbo.tblQualified", codes[0]));
            Assert.Equal(3, await CountAsync("dbo.tblEmployeeFile", codes[0])); Assert.Equal(3, await CountAsync("dbo.tblEmployeeFileMigration", codes[0]));
            Assert.Equal(2, await CountAsync("dbo.tblEmployeeFileAlias", codes[0]));
            var certificateOwner = await repository.GetOwnerStateAsync(new(FileOwnerTable.Qualified, codes[0], skill), default);
            Assert.NotNull(certificateOwner.FileId); Assert.Equal(certificate, certificateOwner.LegacyPath);

            // A new disk manifest still cannot replace an already SQL-authoritative owner.
            await File.WriteAllBytesAsync(Path.Combine(root, photos[codes[0]]), Encoding.ASCII.GetBytes("GIF89a-different generated fixture"));
            var fresh = await new InventoryCommand(new SqlInventoryDatabase(factory)).ExecuteAsync(new[] { root }, default);
            await Assert.ThrowsAsync<DBConcurrencyException>(() => Importer().ExecuteAsync(fresh, Options(codes[0]), default));
            Assert.Equal(applied.FileId, (await repository.GetOwnerStateAsync(new(FileOwnerTable.Employee, codes[0]), default)).FileId);
            Assert.Equal(3, await CountAsync("dbo.tblEmployeeFile", codes[0]));

            // Status changes after the engine precheck are rejected by transactional publication.
            var racing = new ImportCommand(new ResignBeforePublish(new SqlImportStore(factory), codes[4]));
            await Assert.ThrowsAsync<InvalidOperationException>(() => racing.ExecuteAsync(manifest, Options(codes[4]), default));
            Assert.Equal(0, await CountAsync("dbo.tblEmployeeFile", codes[4])); Assert.Equal(0, await CountAsync("dbo.tblEmployeeFileMigration", codes[4]));
            Assert.Null((await repository.GetOwnerStateAsync(new(FileOwnerTable.Employee, codes[4]), default)).FileId);

            await ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=GETDATE() WHERE EmpCode=@employee", codes[0]);
            using (var lease = await repository.OpenReadAsync(applied.FileId.Value, codes[0], FileKind.Photo, default))
                Assert.Equal(SHA256.HashData(photoBytes), await SHA256.HashDataAsync(lease.Content));
            Assert.Equal(3, await CountAsync("dbo.tblEmployeeFile", codes[0]));
        }
        finally
        {
            // Delete only rows created by this fixture, in FK order; source fixtures are generated locally.
            foreach (var code in codes)
            {
                await ExecuteAsync("UPDATE dbo.tblEmployee SET PhotoFileId=NULL WHERE EmpCode=@employee", code);
                await ExecuteAsync("DELETE dbo.tblQualified WHERE EmpCode=@employee", code);
                foreach (var table in new[] { "dbo.tblEmployeeFileAlias", "dbo.tblEmployeeFileMigration", "dbo.tblEmployeeFile" })
                    await ExecuteAsync("DELETE " + table + " WHERE EmpCode=@employee", code);
                await ExecuteAsync("DELETE dbo.tblEmployee WHERE EmpCode=@employee", code);
            }
            await ExecuteAsync("DELETE dbo.tblQualified WHERE OcrSkillRowId=@id", null, null, orphan);
            var fixtureParent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory)) + Path.DirectorySeparatorChar;
            if (!Path.GetFullPath(root).StartsWith(fixtureParent, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
            Directory.Delete(root, true);
        }
    }

    private sealed class ResignBeforePublish(IImportStore inner, string employee) : IImportStore
    {
        public Task<string> DatabaseNameAsync(CancellationToken ct) => inner.DatabaseNameAsync(ct);
        public Task<bool> IsCurrentAsync(string code, CancellationToken ct) => inner.IsCurrentAsync(code, ct);
        public Task<Guid?> FindCommittedAsync(ManifestEntry entry, CancellationToken ct) => inner.FindCommittedAsync(entry, ct);
        public async Task<ImportResult> PublishAsync(ManifestEntry entry, CancellationToken ct)
        {
            await ExecuteAsync("UPDATE dbo.tblEmployee SET ResignDate=GETDATE() WHERE EmpCode=@employee", employee);
            return await inner.PublishAsync(entry, ct);
        }
    }
    private static async Task ExecuteAsync(string sql, string? employee = null, string? value = null, Guid? id = null)
    {
        using var c = new SqlConnection(ConnectionString); await c.OpenAsync(); using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = (object?)employee ?? DBNull.Value;
        cmd.Parameters.Add("@value", SqlDbType.NVarChar, 100).Value = (object?)value ?? DBNull.Value;
        cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = (object?)id ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync();
    }
    private static async Task<int> CountAsync(string table, string employee)
    {
        using var c = new SqlConnection(ConnectionString); await c.OpenAsync();
        using var cmd = new SqlCommand("SELECT COUNT(*) FROM " + table + " WHERE EmpCode=@employee", c);
        cmd.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee; return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }
}
