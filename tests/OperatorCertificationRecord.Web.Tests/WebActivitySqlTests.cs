using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class WebActivitySqlFactAttribute : FactAttribute
{
    public WebActivitySqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OCR_ACTIVITY_TEST_CONNECTION")))
            Skip = "Set OCR_ACTIVITY_TEST_CONNECTION to a marked disposable OcrActivityTests_* LocalDB database.";
    }
}
[CollectionDefinition("WebActivitySql", DisableParallelization = true)]
public sealed class WebActivitySqlCollection { }

[Collection("WebActivitySql")]
public sealed class WebActivitySqlTests
{
    [WebActivitySqlFact]
    public async Task SqlChangeAndActorSnapshotCommitTogetherWithoutEarlyHttpSuccess()
    {
        using var f = await Fixture.CreateAsync();
        using var connection = Connection(); await connection.OpenAsync();
        using var tx = connection.BeginTransaction(IsolationLevel.Serializable);
        var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
        await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET JobGrade=N'52T' WHERE EmpCode=N'0012345'");
        var id = await f.Writer.CompleteAsync(connection, tx, capture);
        Assert.False(f.Http.Items.ContainsKey(WebActivityContextFactory.RecordedKey));
        tx.Commit(); f.Writer.MarkCommitted();
        Assert.Equal(true, f.Http.Items[WebActivityContextFactory.RecordedKey]);
        var detail = await f.Reader.DetailAsync("101", id);
        Assert.NotNull(detail); Assert.Equal("101", detail!.Event.ActorCode); Assert.True(detail.Event.ActorIsDeveloper);
        var changed = Assert.Single(Assert.Single(detail.Changes).Fields);
        Assert.Equal("JobGrade", changed.Field); Assert.Equal("51T", changed.Before); Assert.Equal("52T", changed.After);
        Assert.Equal(1, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task MissingAuditStorageRollsBackTheBusinessWrite()
    {
        using var f = await Fixture.CreateAsync();
        using var connection = Connection(); await connection.OpenAsync();
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
            await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET Shift=N'B' WHERE EmpCode=N'0012345'; DROP TABLE dbo.tblWebActivityChange");
            await Assert.ThrowsAsync<SqlException>(() => f.Writer.CompleteAsync(connection, tx, capture));
            tx.Rollback();
        }
        Assert.Equal("A", await f.ValueAsync("SELECT Shift FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(0, await f.CountAsync("dbo.tblWebActivityEvent"));
        Assert.False(f.Http.Items.ContainsKey(WebActivityContextFactory.RecordedKey));
    }

    [WebActivitySqlFact]
    public async Task ReplaysAreCheckedBeforeDmlAndChangedCommandsAreRejected()
    {
        using var f = await Fixture.CreateAsync();
        using var connection = Connection(); await connection.OpenAsync();
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
            await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET Shift=N'B' WHERE EmpCode=N'0012345'");
            await f.Writer.CompleteAsync(connection, tx, capture); tx.Commit();
        }
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            var replay = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
            Assert.True(replay.Replayed); Assert.Equal(1, replay.ExistingChangesCount); tx.Rollback();
        }
        f.Http.Items["WebActivity.CommandFingerprint"] = new string('F', 64);
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.CaptureAsync(connection, tx,
                "Employee", "employee-update", [WebActivityScope.Employee("0012345")])); tx.Rollback();
        }
        Assert.Equal(1, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task CommandReceiptRejectsChangedTargetsAcrossDifferentItemParts()
    {
        using var f = await Fixture.CreateAsync();
        f.Http.Items["WebActivity.CommandFingerprint"] = new string('A', 64);
        using var connection = Connection(); await connection.OpenAsync();
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Skills", "skill-update", [WebActivityScope.Skills("0012345", "A")]);
            await ExecuteAsync(connection, tx, "UPDATE dbo.tblQualified SET Remark=N'Changed A' WHERE ProcessName=N'A'");
            await f.Writer.CompleteAsync(connection, tx, capture); tx.Commit();
        }
        // Same protected whole-form command may legitimately finish another selected item.
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            var part = await f.Writer.CaptureAsync(connection, tx, "Skills", "skill-update", [WebActivityScope.Skills("0012345", "B")]);
            Assert.False(part.Replayed); tx.Rollback();
        }
        // Reusing its identity with another posted target/patch must fail before DML,
        // even though that target would generate a different per-item receipt key.
        f.Http.Items["WebActivity.CommandFingerprint"] = new string('B', 64);
        using (var tx = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.CaptureAsync(connection, tx,
                "Employee", "employee-update", [WebActivityScope.Employee("0000002")]));
            tx.Rollback();
        }
        Assert.Equal(1, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task PartialSkillCommandPartsHaveSeparateReceiptsAndSafeFileMetadata()
    {
        using var f = await Fixture.CreateAsync();
        using var connection = Connection(); await connection.OpenAsync();
        foreach (var process in new[] { "A", "B" })
        {
            using var tx = connection.BeginTransaction(IsolationLevel.Serializable);
            var capture = await f.Writer.CaptureAsync(connection, tx, "Skills", "skill-update", [WebActivityScope.Skills("0012345", process)]);
            Assert.False(capture.Replayed);
            using var change = new SqlCommand("UPDATE dbo.tblQualified SET Remark=N'Changed',Download=CASE WHEN ProcessName=N'A' THEN N'\\other\new.pdf' ELSE Download END WHERE ProcessName=@process", connection, tx);
            change.Parameters.AddWithValue("@process", process); await change.ExecuteNonQueryAsync();
            await f.Writer.CompleteAsync(connection, tx, capture); tx.Commit();
        }
        Assert.Equal(2, await f.CountAsync("dbo.tblWebActivityEvent"));
        var page = await f.Reader.ListAsync("101", new() { From = new(2020, 1, 1), To = new(2030, 1, 1), Skill = "A" });
        Assert.Single(page.Rows); Assert.Equal("A", page.Rows[0].SkillName);
        var allJson = await f.ValueAsync("SELECT FieldsJson FROM dbo.tblWebActivityChange WHERE SkillName=N'A'");
        Assert.Contains("PdfFileName", allJson); Assert.DoesNotContain("private", allJson); Assert.DoesNotContain("password-secret", allJson);
    }

    [WebActivitySqlFact]
    public async Task OutboxDeliveryIsIdempotentButIdentityCannotBeReusedForAnotherChange()
    {
        using var f = await Fixture.CreateAsync();
        var actor = f.Contexts.Current(); var uid = Guid.NewGuid();
        var change = new WebActivityChange("Role", "0012345:Admin", "0012345", null, null, "Administrator", true, true,
            [new("Admin", "false", "true")]);
        var record = new WebActivityRecord(uid, actor, "Roles", "admin-enable", "Committed", "Give Admin", [change], DateTimeOffset.UtcNow, "RoleFile", uid);
        var first = await f.Writer.RecordStandaloneAsync(record);
        Assert.Equal(first, await f.Writer.RecordStandaloneAsync(record));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Writer.RecordStandaloneAsync(record with
        { Changes = [change with { Fields = [new("Admin", "true", "false")] }] }));
        Assert.Equal(1, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task PasswordMarkerNeverPersistsItsValueAndNoOpIsNotAChange()
    {
        using var f = await Fixture.CreateAsync();
        using var connection = Connection(); await connection.OpenAsync();
        using (var tx = connection.BeginTransaction())
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "password-change", [WebActivityScope.Employee("0012345")]);
            await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET EmpPassword=N'new-secret' WHERE EmpCode=N'0012345'");
            await f.Writer.CompleteAsync(connection, tx, capture, passwordChanged: true); tx.Commit();
        }
        var json = await f.ValueAsync("SELECT FieldsJson FROM dbo.tblWebActivityChange");
        Assert.Contains("PasswordChanged", json); Assert.DoesNotContain("secret", json); Assert.DoesNotContain("EmpPassword", json);
        f.Http.Items["WebActivity.CommandId"] = Guid.NewGuid();
        using (var tx = connection.BeginTransaction())
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
            await f.Writer.CompleteAsync(connection, tx, capture); tx.Commit();
        }
        var changes = await f.Reader.ListAsync("101", new() { From = new(2020, 1, 1), To = new(2030, 1, 1) });
        Assert.Single(changes.Rows);
        var activity = await f.Reader.ListAsync("101", new() { Tab = "activity", From = new(2020, 1, 1), To = new(2030, 1, 1) });
        Assert.Single(activity.Rows); Assert.Equal("Committed", activity.Rows[0].Outcome);
    }

    [WebActivitySqlFact]
    public async Task HistoricalMetadataStaysUnknownAndBangkokDateBoundsAreCorrect()
    {
        using var f = await Fixture.CreateAsync();
        var actor = new WebActivityActor("old-admin", null, null, null, Guid.NewGuid(), "", "");
        foreach (var time in new[] { new DateTimeOffset(2025, 12, 31, 16, 59, 0, TimeSpan.Zero), new DateTimeOffset(2025, 12, 31, 17, 0, 0, TimeSpan.Zero) })
            await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), actor, "Employee", "employee-update", "Committed", "Update employee",
                [new("Employee", "0012345", "0012345", null, null, "Employee", true, true, [new("Shift", "A", "B")])], time, "Historical"));
        var page = await f.Reader.ListAsync("101", new() { Tab = "activity", From = new(2026, 1, 1), To = new(2026, 1, 1), Actor = "old-admin" });
        var row = Assert.Single(page.Rows); Assert.Null(row.ActorName); Assert.Null(row.ActorIsAdmin); Assert.Null(row.ActorIsDeveloper);
        Assert.Equal(17, row.OccurredUtc.Hour);
    }

    [WebActivitySqlFact]
    public async Task EmployeeTimelineUsesAffectedEmployeeRatherThanAdminAndExcludesReadObservations()
    {
        using var f = await Fixture.CreateAsync();
        var admin = f.Contexts.Current();
        var otherAdmin = admin with { ActorCode = "102", ActorName = "ADMIN2" };
        var time = new DateTimeOffset(2026, 10, 29, 3, 0, 0, TimeSpan.Zero);
        static WebActivityChange Employee(string code, string type = "Employee") =>
            new(type, code, code, null, null, code, true, true, [new("Shift", "A", "B")]);
        await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), admin, "Employee", "employee-update", "Committed",
            "Admin 101 changed another employee", [Employee("999")], time, "Fixture"));
        var first = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), otherAdmin, "Employee", "employee-update", "Committed",
            "Employee 101 changed", [Employee("101")], time, "Fixture"));
        var second = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), otherAdmin, "Employee", "employee-transfer", "Partial",
            "Some employee 101 records saved", [Employee("101")], time, "Fixture"));
        var newest = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), otherAdmin, "Employee", "employee-update", "NoChange",
            "Recorded employee target without a change", [Employee("101") with { Fields = [] }], time.AddMinutes(1), "Fixture"));
        await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), otherAdmin, "Employee", "employee-update", "Committed",
            "Different code with leading zeros", [Employee("0000101")], time.AddMinutes(2), "Fixture"));
        foreach (var observation in new[] { ("page-view", "Viewed"), ("report-ready", "Prepared"), ("login", "Succeeded"), ("request", "Committed") })
            await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), admin, "Activity", observation.Item1, observation.Item2,
                "Access observation", [Employee("101", "Access")], time.AddMinutes(3), "Fixture"));
        await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), admin, "Catalog", "process-add", "Committed", "Catalog only",
            [new("Process", "catalog", null, "A", null, "A", false, true, [new("ProcessName", null, "A")])], time, "Fixture"));

        var timeline = await f.Reader.ListAsync("101", new() { Tab = "activity", Employee = "101", From = time.Date, To = time.Date });
        Assert.Equal(new[] { newest, second, first }, timeline.Rows.Select(row => row.EventId));
        Assert.All(timeline.Rows, row => { Assert.Equal("101", row.EmployeeCode); Assert.Equal("102", row.ActorCode); });
        Assert.Empty((await f.Reader.ListAsync("101", new() { Tab = "activity", Employee = "101", Actor = "101", From = time.Date, To = time.Date })).Rows);
        var padded = Assert.Single((await f.Reader.ListAsync("101", new() { Tab = "activity", Employee = "0000101", From = time.Date, To = time.Date })).Rows);
        Assert.Equal("0000101", padded.EmployeeCode);
        var all = await f.Reader.ListAsync("101", new() { Tab = "activity", From = time.Date, To = time.Date });
        Assert.Equal(5, all.Rows.Count);
        Assert.DoesNotContain("page-view", all.Actions); Assert.DoesNotContain("report-ready", all.Actions);
    }

    [WebActivitySqlFact]
    public async Task EmployeeAndSkillFiltersProjectTheMatchingBulkOrMoveItemTogether()
    {
        using var f = await Fixture.CreateAsync();
        var time = new DateTimeOffset(2026, 10, 29, 3, 0, 0, TimeSpan.Zero);
        var other = new WebActivityChange("Skill", "source", "999", "Source process", "Current", "Source process",
            true, false, [new("EmpCode", "999", null)]);
        var selected = new WebActivityChange("Skill", "target", "101", "Target process", "Current", "Target process",
            false, true, [new("EmpCode", null, "101")]);
        var id = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), f.Contexts.Current(), "Recovery", "move", "Committed",
            "Move between employees", [other, selected], time, "Fixture"));
        foreach (var tab in new[] { "activity", "changes" })
        {
            var row = Assert.Single((await f.Reader.ListAsync("101", new() { Tab = tab, Employee = "101", From = time.Date, To = time.Date })).Rows);
            Assert.Equal(id, row.EventId); Assert.Equal("101", row.EmployeeCode); Assert.Equal("Target process", row.SkillName);
            var bySkill = Assert.Single((await f.Reader.ListAsync("101", new() { Tab = tab, Skill = "Target", From = time.Date, To = time.Date })).Rows);
            Assert.Equal("101", bySkill.EmployeeCode); Assert.Equal("Target process", bySkill.SkillName);
            Assert.Single((await f.Reader.ListAsync("101", new() { Tab = tab, Employee = "101", Skill = "Target", From = time.Date, To = time.Date })).Rows);
            Assert.Empty((await f.Reader.ListAsync("101", new() { Tab = tab, Employee = "101", Skill = "Source", From = time.Date, To = time.Date })).Rows);
        }
    }

    [WebActivitySqlFact]
    public async Task ReadFiltersTreatWildcardsAsLiteralAndHonorEmployeeNameAndCatalogLabels()
    {
        using var f = await Fixture.CreateAsync();
        var actor = f.Contexts.Current();
        await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), actor, "Catalog", "process-add", "Committed", "Add process",
            [new("Process", "synthetic", null, "A_100%", null, "A_100%", false, true, [new("ProcessName", null, "A_100%")])], DateTimeOffset.UtcNow, "Fixture"));
        Assert.Single((await f.Reader.ListAsync("101", new() { Skill = "_100%" })).Rows);
        Assert.Empty((await f.Reader.ListAsync("101", new() { Employee = "0012345" })).Rows);
        Assert.Single((await f.Reader.ListAsync("101", new() { Actor = "Test Developer" })).Rows);
    }

    [WebActivitySqlFact]
    public async Task EmployeeRemovalCannotCascadeIntoHistoryAndMigrationCanBeReapplied()
    {
        using var f = await Fixture.CreateAsync();
        var actor = f.Contexts.Current();
        var id = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), actor, "Employee", "employee-add", "Committed", "Add employee",
            [new("Employee", "0000002", "0000002", null, null, "Former employee", false, true, [new("EmpCode", null, "0000002")])], DateTimeOffset.UtcNow, "Fixture"));
        await f.RunAsync("INSERT dbo.tblEmployee(EmpCode) VALUES(N'0000002'); DELETE dbo.tblEmployee WHERE EmpCode=N'0000002'");
        await f.RunAsync(Migration());
        Assert.NotNull(await f.Reader.DetailAsync("101", id)); Assert.Equal(1, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task RecoveryHistoryUsesOriginalSourceEvenWhenFirstChangeBelongsToDestination()
    {
        using var f = await Fixture.CreateAsync();
        var action = Guid.NewGuid();
        await f.RunAsync("CREATE TABLE dbo.tblSkillRecoveryAction(ActionId uniqueidentifier PRIMARY KEY,SourceEmployee nvarchar(20)); INSERT dbo.tblSkillRecoveryAction VALUES('" + action.ToString("D") + "',N'0012345')");
        var target = new WebActivityChange("Skill", Guid.NewGuid().ToString("D"), "0012346", "Process A", "Current", "Process A",
            false, true, [new("EmpCode", null, "0012346")]);
        var id = await f.Writer.RecordStandaloneAsync(new(Guid.NewGuid(), f.Contexts.Current(), "Recovery", "move", "Committed",
            "Move skill", [target], DateTimeOffset.UtcNow, "SkillRecovery", action, RecoveryActionId: action));
        var detail = await f.Reader.DetailAsync("101", id);
        Assert.NotNull(detail);
        Assert.Equal("0012346", detail!.Event.EmployeeCode);
        Assert.Equal("0012345", detail.RecoveryEmployeeCode);
    }

    [WebActivitySqlFact]
    public async Task RoleRevocationBeforeCompletionRollsBackTheChange()
    {
        using var f = await Fixture.CreateAsync();
        f.SetRoles(admins: "[\"101\"]", developers: "[\"103\"]");
        using var connection = Connection(); await connection.OpenAsync();
        using (var tx = connection.BeginTransaction())
        {
            var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
            await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET JobGrade=N'52T' WHERE EmpCode=N'0012345'");
            f.SetRoles(admins: "[]", developers: "[\"103\"]");
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Writer.CompleteAsync(connection, tx, capture));
            tx.Rollback();
        }
        Assert.Equal("51T", await f.ValueAsync("SELECT JobGrade FROM dbo.tblEmployee WHERE EmpCode=N'0012345'"));
        Assert.Equal(0, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    [WebActivitySqlFact]
    public async Task ReaderChecksDeveloperRoleAgainBeforeReturningResults()
    {
        using var f = await Fixture.CreateAsync();
        var reader = new WebActivityReader(f.Configuration, f.Developers, f.Contexts,
            new CallbackClock(() => f.SetRoles("[\"101\"]", "[]")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => reader.ListAsync("101", new()));
    }

    [WebActivitySqlFact]
    public async Task LegacyBinaryPhotosAreNeverReadIntoAuditSnapshots()
    {
        using var f = await Fixture.CreateAsync();
        await f.RunAsync("ALTER TABLE dbo.tblEmployee DROP COLUMN Photo");
        await f.RunAsync("ALTER TABLE dbo.tblEmployee ADD Photo varbinary(max)");
        await f.RunAsync("UPDATE dbo.tblEmployee SET Photo=0x736563726574696D616765");
        using var connection = Connection(); await connection.OpenAsync();
        using var tx = connection.BeginTransaction();
        var capture = await f.Writer.CaptureAsync(connection, tx, "Employee", "employee-update", [WebActivityScope.Employee("0012345")]);
        Assert.DoesNotContain("Photo", Assert.Single(capture.Before).Fields.Keys);
        await ExecuteAsync(connection, tx, "UPDATE dbo.tblEmployee SET Shift=N'B'");
        await f.Writer.CompleteAsync(connection, tx, capture); tx.Commit();
        var json = await f.ValueAsync("SELECT FieldsJson FROM dbo.tblWebActivityChange");
        Assert.DoesNotContain("secretimage", json); Assert.DoesNotContain("Photo", json);
    }

    [WebActivitySqlFact]
    public async Task AuthenticatedPostCannotBeginMutationWithoutValidatedCommandIdentity()
    {
        using var f = await Fixture.CreateAsync();
        f.Http.Request.Method = "POST"; f.Http.Items.Remove("WebActivity.CommandId");
        using var connection = Connection(); await connection.OpenAsync();
        using var tx = connection.BeginTransaction();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Writer.CaptureAsync(connection, tx,
            "Employee", "employee-update", [WebActivityScope.Employee("0012345")]));
        tx.Rollback();
        Assert.Equal("A", await f.ValueAsync("SELECT Shift FROM dbo.tblEmployee"));
        Assert.Equal(0, await f.CountAsync("dbo.tblWebActivityEvent"));
    }

    private static async Task ExecuteAsync(SqlConnection connection, SqlTransaction tx, string sql)
    { using var command = new SqlCommand(sql, connection, tx); await command.ExecuteNonQueryAsync(); }
    private static SqlConnection Connection()
    {
        var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_ACTIVITY_TEST_CONNECTION") ?? "");
        if (!(builder.DataSource.Equals("(localdb)\\CodexSkillRecovery", StringComparison.OrdinalIgnoreCase) || builder.DataSource.Equals("(localdb)\\MSSQLLocalDB", StringComparison.OrdinalIgnoreCase)) ||
            !System.Text.RegularExpressions.Regex.IsMatch(builder.InitialCatalog, "^OcrActivityTests_[A-Za-z0-9_]+$"))
            throw new InvalidOperationException("Activity integration tests require their marked disposable LocalDB fixture.");
        return new(builder.ConnectionString);
    }
    private static string Migration()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "deploy", "sql", "20261029_web_activity.sql"))) directory = directory.Parent;
        return directory == null ? throw new InvalidOperationException("Activity migration was not found.") : File.ReadAllText(Path.Combine(directory.FullName, "deploy", "sql", "20261029_web_activity.sql"));
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string _roles = Path.Combine(Path.GetTempPath(), "OcrActivityCoreRoles_" + Guid.NewGuid().ToString("N"));
        private readonly ServiceProvider _provider;
        public DefaultHttpContext Http { get; }
        public WebActivityContextFactory Contexts { get; }
        public WebActivityWriter Writer { get; }
        public WebActivityReader Reader { get; }
        public IConfiguration Configuration { get; }
        public DeveloperRoleService Developers { get; }
        private Fixture()
        {
            Directory.CreateDirectory(_roles); File.WriteAllText(Path.Combine(_roles, "admins.json"), "[\"101\"]");
            File.WriteAllText(Path.Combine(_roles, "developers.json"), "[\"101\",\"103\"]");
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["ConnectionStrings:DefaultConnection"] = Connection().ConnectionString,
              ["AdminRoles:StorePath"] = Path.Combine(_roles, "admins.json"), ["DeveloperRoles:StorePath"] = Path.Combine(_roles, "developers.json") }).Build();
            var roles = new DeveloperRoleService(config, NullLogger<DeveloperRoleService>.Instance);
            Configuration = config; Developers = roles;
            var admins = new AdminService(config, NullLogger<AdminService>.Instance, roles);
            _provider = new ServiceCollection().AddSingleton(roles).AddSingleton(admins).BuildServiceProvider();
            Http = new DefaultHttpContext { RequestServices = _provider, TraceIdentifier = "activity-test" };
            Http.Features.Set<ISessionFeature>(new SessionFeature()); Http.Session.SetString("UserCode", "101"); Http.Session.SetString("UserName", "Test Developer");
            Http.Items["WebActivity.CommandId"] = Guid.NewGuid();
            Contexts = new(new FixedAccessor(Http)); Writer = new(config, Contexts); Reader = new(config, roles, Contexts);
        }
        public static async Task<Fixture> CreateAsync()
        {
            using var connection = Connection(); await connection.OpenAsync();
            using var marker = new SqlCommand("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'", connection);
            if ((string?)await marker.ExecuteScalarAsync() != "SyntheticLocalFixture") throw new InvalidOperationException("The disposable activity fixture marker is required.");
            using (var setup = new SqlCommand(Schema, connection)) await setup.ExecuteNonQueryAsync();
            using (var setup = new SqlCommand(Migration(), connection)) await setup.ExecuteNonQueryAsync();
            return new();
        }
        public async Task RunAsync(string sql) { using var connection = Connection(); await connection.OpenAsync(); using var command = new SqlCommand(sql, connection); await command.ExecuteNonQueryAsync(); }
        public async Task<string> ValueAsync(string sql) { using var connection = Connection(); await connection.OpenAsync(); using var command = new SqlCommand(sql, connection); return Convert.ToString(await command.ExecuteScalarAsync()) ?? ""; }
        public async Task<int> CountAsync(string table) => int.Parse(await ValueAsync("SELECT COUNT(*) FROM " + table), System.Globalization.CultureInfo.InvariantCulture);
        public void SetRoles(string admins, string developers)
        {
            File.WriteAllText(Path.Combine(_roles, "admins.json"), admins);
            File.WriteAllText(Path.Combine(_roles, "developers.json"), developers);
        }
        public void Dispose()
        {
            _provider.Dispose();
            var expected = Path.Combine(Path.GetTempPath(), "OcrActivityCoreRoles_");
            if (Path.GetFullPath(_roles).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) Directory.Delete(_roles, true);
        }
        private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = new TestSession(); }
        private sealed class FixedAccessor(HttpContext http) : IHttpContextAccessor
        {
            public HttpContext? HttpContext { get; set; } = http;
        }
    }
    private sealed class CallbackClock(Action callback) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() { callback(); return DateTimeOffset.UtcNow; }
    }
    private const string Schema = """
        IF OBJECT_ID(N'dbo.tblWebActivityChange',N'U') IS NOT NULL DROP TABLE dbo.tblWebActivityChange;
        IF OBJECT_ID(N'dbo.tblWebActivityEvent',N'U') IS NOT NULL DROP TABLE dbo.tblWebActivityEvent;
        IF OBJECT_ID(N'dbo.tblSkillRecoveryAction',N'U') IS NOT NULL DROP TABLE dbo.tblSkillRecoveryAction;
        IF OBJECT_ID(N'dbo.tblQualified_Obsoleted',N'U') IS NOT NULL DROP TABLE dbo.tblQualified_Obsoleted;
        IF OBJECT_ID(N'dbo.tblQualified',N'U') IS NOT NULL DROP TABLE dbo.tblQualified;
        IF OBJECT_ID(N'dbo.tblEmployeeFile',N'U') IS NOT NULL DROP TABLE dbo.tblEmployeeFile;
        IF OBJECT_ID(N'dbo.tblEmployee',N'U') IS NOT NULL DROP TABLE dbo.tblEmployee;
        CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(20) PRIMARY KEY,EmpPassword nvarchar(100),PersonFnameEng nvarchar(50),PersonLnameEng nvarchar(50),JobGrade nvarchar(20),Shift nvarchar(10),Photo nvarchar(500),PhotoFileId uniqueidentifier);
        INSERT dbo.tblEmployee VALUES(N'0012345',N'password-secret',N'Synthetic',N'Employee',N'51T',N'A',N'\\private\photos\photo.jpg',NULL);
        CREATE TABLE dbo.tblEmployeeFile(FileId uniqueidentifier PRIMARY KEY,EmpCode nvarchar(20),Kind nvarchar(16),OriginalFileName nvarchar(255),ByteLength bigint,Sha256 binary(32),Content varbinary(max));
        CREATE TABLE dbo.tblQualified(EmpCode nvarchar(20),OcrSkillRowId uniqueidentifier UNIQUE,ProcessName nvarchar(100),CertifiedDate datetime,Remark nvarchar(1000),Download nvarchar(1000),CertificateFileId uniqueidentifier,FileStorageVersion rowversion);
        INSERT dbo.tblQualified(EmpCode,OcrSkillRowId,ProcessName,CertifiedDate,Remark,Download) VALUES(N'0012345',NEWID(),N'A','20260101',N'Original',N'\\private\certs\a.pdf'),(N'0012345',NEWID(),N'B','20260101',N'Original',NULL);
        SELECT TOP(0) * INTO dbo.tblQualified_Obsoleted FROM dbo.tblQualified;
        """;
}
