using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AccessRoleSqlFactAttribute : FactAttribute
{
    public AccessRoleSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_ACCESS_ROLES_TEST_CONNECTION")))
            Skip = "Set OCR_ACCESS_ROLES_TEST_CONNECTION to a marked isolated OcrAccessRoleTests_ LocalDB database.";
    }
}

[CollectionDefinition("AccessRoleSql", DisableParallelization = true)]
public sealed class AccessRoleSqlCollection { }

[Collection("AccessRoleSql")]
public sealed class RoleActivitySqlMigrationTests
{
    [Fact]
    public void Missing_source_is_rejected_before_any_database_access()
    {
        using var files = new SourceFiles();
        File.Delete(files.Developers);
        Assert.Throws<FileNotFoundException>(() => RoleActivitySqlMigration.Apply(
            new ConfigurationBuilder().Build(), files.Developers, files.Admins));
    }

    [Fact]
    public void Same_file_cannot_be_imported_as_both_roles()
    {
        using var files = new SourceFiles();
        Assert.Throws<IOException>(() => RoleActivitySqlMigration.Apply(
            new ConfigurationBuilder().Build(), files.Developers, files.Developers));
    }

    [Fact]
    public void Missing_database_configuration_does_not_change_source_files()
    {
        using var files = new SourceFiles();
        Assert.Throws<IOException>(() => RoleActivitySqlMigration.Apply(
            new ConfigurationBuilder().Build(), files.Developers, files.Admins));
        Assert.Equal("[\"101\"]", File.ReadAllText(files.Developers));
        Assert.Equal("[\"0012345\"]", File.ReadAllText(files.Admins));
    }

    [AccessRoleSqlFact]
    public void Import_preserves_leading_zero_codes_audit_events_and_command_receipts()
    {
        using var fixture = new Fixture();
        var uid = Guid.NewGuid();
        var actor = new WebActivityActor("101", "Fixture", true, true, Guid.NewGuid(), "fixture", "/Admins");
        var change = new WebActivityChange("access-role", "0012345:developer", "0012345", null, null,
            "Developer access", true, true, [new("Developer", "false", "true")]);
        var record = new WebActivityRecord(uid, actor, "roles", "developer-enable", "Committed",
            "Enable Developer", [change], DateTimeOffset.UtcNow, "RoleFile", uid);
        var receipt = new RoleCommandReceipt(actor.OperationId, "101", "0012345", "Developer", true,
            new string('a', 64), "Updated", DateTimeOffset.UtcNow.AddHours(1));
        var expected = new RoleActivityEnvelope(1, 8, ["101", "0012345"], [record], [receipt]);
        File.WriteAllText(fixture.Files.Developers, JsonSerializer.Serialize(expected));

        var result = fixture.Apply();

        Assert.True(result.Imported);
        Assert.Equal(2, result.DeveloperCount);
        Assert.Equal(1, result.AdminCount);
        var actual = RoleActivityFileStore.Parse(fixture.Envelope("Developer"));
        Assert.Equal(8, actual.MembershipRevision);
        Assert.Equal(uid, Assert.Single(actual.PendingAuditEvents).EventUid);
        Assert.Equal(actor.OperationId, Assert.Single(actual.ConsumedCommands!).CommandId);
        Assert.Equal(new[] { "0012345", "101" }, actual.Members);
        Assert.Equal(new[] { "0012345", "101" }, fixture.Members("Developer"));
    }

    [AccessRoleSqlFact]
    public void Repeat_never_reimports_stale_files_over_new_sql_membership()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Apply().Imported);
        var updated = new RoleActivityEnvelope(1, 1, ["101", "0099999"], [], []);
        fixture.Execute("UPDATE dbo.tblApplicationRoleStore SET Envelope=@json WHERE RoleName=N'Developer'; " +
            "INSERT dbo.tblApplicationRoleMember(RoleName,EmpCode) VALUES(N'Developer',N'0099999')",
            new SqlParameter("@json", SqlDbType.NVarChar, -1) { Value = JsonSerializer.Serialize(updated) });

        var result = fixture.Apply();

        Assert.False(result.Imported);
        Assert.Equal(2, result.DeveloperCount);
        Assert.Equal(new[] { "0099999", "101" }, fixture.Members("Developer"));
    }

    [AccessRoleSqlFact]
    public void Partial_initialization_and_projection_mismatch_are_rejected()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        fixture.Execute("DELETE dbo.tblApplicationRoleMember WHERE RoleName=N'Admin'; DELETE dbo.tblApplicationRoleStore WHERE RoleName=N'Admin'");
        Assert.Throws<IOException>(() => fixture.Apply());
        Assert.Equal(new[] { "101" }, fixture.Members("Developer"));
    }

    [AccessRoleSqlFact]
    public void Mismatched_member_projection_cannot_be_silently_repaired_or_reseeded()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        fixture.Execute("DELETE dbo.tblApplicationRoleMember WHERE RoleName=N'Developer'");
        Assert.Throws<IOException>(() => fixture.Apply());
        Assert.Empty(fixture.Members("Developer"));
    }

    [AccessRoleSqlFact]
    public void Unsupported_existing_schema_is_not_modified_or_treated_as_an_empty_store()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        fixture.Execute("ALTER TABLE dbo.tblApplicationRoleMember ADD UnexpectedColumn int NULL");
        Assert.Throws<IOException>(() => fixture.Apply());
        Assert.Equal(new[] { "101" }, fixture.Members("Developer"));
        Assert.Equal(2, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM dbo.tblApplicationRoleStore")));
    }

    [AccessRoleSqlFact]
    public void Invalid_second_source_leaves_schema_uncreated()
    {
        using var fixture = new Fixture();
        File.WriteAllText(fixture.Files.Admins, "invalid");
        Assert.Throws<IOException>(() => fixture.Apply());
        Assert.Equal(DBNull.Value, fixture.Scalar("SELECT OBJECT_ID(N'dbo.tblApplicationRoleStore',N'U')"));
    }

    [AccessRoleSqlFact]
    public void Failure_saving_second_role_rolls_back_first_role_and_members()
    {
        using var fixture = new Fixture();
        fixture.Apply();
        fixture.Execute("DELETE dbo.tblApplicationRoleMember; DELETE dbo.tblApplicationRoleStore");
        fixture.Execute("CREATE TRIGGER dbo.OcrFixtureRejectAdmin ON dbo.tblApplicationRoleStore AFTER INSERT AS " +
            "BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE RoleName=N'Admin') RAISERROR('Synthetic Admin insert failure',16,1); END");
        Assert.Throws<IOException>(() => fixture.Apply());
        Assert.Equal(0, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM dbo.tblApplicationRoleStore")));
        Assert.Equal(0, Convert.ToInt32(fixture.Scalar("SELECT COUNT(*) FROM dbo.tblApplicationRoleMember")));
    }

    private sealed class SourceFiles : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OcrAccessRoleMigration_" + Guid.NewGuid().ToString("N"));
        public string Developers => Path.Combine(_directory, "developers.json");
        public string Admins => Path.Combine(_directory, "admins.json");
        public SourceFiles()
        {
            Directory.CreateDirectory(_directory);
            File.WriteAllText(Developers, "[\"101\"]");
            File.WriteAllText(Admins, "[\"0012345\"]");
        }
        public void Dispose() { foreach (var file in Directory.EnumerateFiles(_directory)) File.Delete(file); Directory.Delete(_directory); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _connection;
        public SourceFiles Files { get; } = new();
        public IConfiguration Configuration { get; }
        public Fixture()
        {
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_ACCESS_ROLES_TEST_CONNECTION") ?? "");
            if (builder.DataSource is not ("(localdb)\\CodexSkillRecovery" or "(localdb)\\MSSQLLocalDB") ||
                !builder.InitialCatalog.StartsWith("OcrAccessRoleTests_", StringComparison.Ordinal))
                throw new InvalidOperationException("Only a disposable local access-role fixture is permitted.");
            _connection = builder.ConnectionString;
            var marker = Scalar("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'");
            if (!Equals(marker, "SyntheticLocalFixture")) throw new InvalidOperationException("The synthetic fixture marker is required.");
            Execute("IF OBJECT_ID(N'dbo.tblApplicationRoleMember',N'U') IS NOT NULL DROP TABLE dbo.tblApplicationRoleMember; " +
                "IF OBJECT_ID(N'dbo.tblApplicationRoleStore',N'U') IS NOT NULL DROP TABLE dbo.tblApplicationRoleStore");
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                { ["ConnectionStrings:DefaultConnection"] = _connection }).Build();
        }
        public RoleActivitySqlMigrationResult Apply() => RoleActivitySqlMigration.Apply(Configuration, Files.Developers, Files.Admins);
        public object? Scalar(string sql)
        {
            using var connection = new SqlConnection(_connection); connection.Open();
            using var command = new SqlCommand(sql, connection); return command.ExecuteScalar();
        }
        public void Execute(string sql, params SqlParameter[] parameters)
        {
            using var connection = new SqlConnection(_connection); connection.Open();
            using var command = new SqlCommand(sql, connection); command.Parameters.AddRange(parameters); command.ExecuteNonQuery();
        }
        public string Envelope(string role)
        {
            using var connection = new SqlConnection(_connection); connection.Open();
            using var command = new SqlCommand("SELECT Envelope FROM dbo.tblApplicationRoleStore WHERE RoleName=@role", connection);
            command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
            return (string)command.ExecuteScalar()!;
        }
        public string[] Members(string role)
        {
            using var connection = new SqlConnection(_connection); connection.Open();
            using var command = new SqlCommand("SELECT EmpCode FROM dbo.tblApplicationRoleMember WHERE RoleName=@role ORDER BY EmpCode", connection);
            command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
            using var reader = command.ExecuteReader(); var result = new List<string>();
            while (reader.Read()) result.Add(reader.GetString(0)); return result.ToArray();
        }
        public void Dispose() => Files.Dispose();
    }
}
