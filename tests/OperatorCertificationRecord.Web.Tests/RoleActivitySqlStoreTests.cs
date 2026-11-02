using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

[Collection("AccessRoleSql")]
public sealed class RoleActivitySqlStoreTests
{
    [AccessRoleSqlFact]
    public void Grants_update_the_SQL_authority_projection_and_audit_without_changing_files()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Updated, fixture.SetDeveloper("0012345", true));
        Assert.Equal(new[] { "0012345", "101", "103" }, fixture.Developers.GetAllDevelopersForReports());
        Assert.True(fixture.Developers.IsDeveloper("0012345"));
        Assert.Equal(RoleChangeResult.Updated, fixture.SetAdmin("0012345", true));
        Assert.True(fixture.Admins.IsAdmin("0012345"));
        Assert.Equal("developer-enable", Assert.Single(fixture.Read("Developer").PendingAuditEvents).Action);
        Assert.Equal("admin-enable", Assert.Single(fixture.Read("Admin").PendingAuditEvents).Action);
        Assert.Equal("[\"101\",\"103\"]", File.ReadAllText(fixture.DeveloperPath));
    }

    [AccessRoleSqlFact]
    public void Consumed_grant_replay_does_not_restore_a_later_revoked_role()
    {
        using var fixture = new Fixture();
        fixture.SetDeveloper("104", true);
        var command = fixture.Http.Items["WebActivity.CommandId"];
        fixture.SetDeveloper("104", false);
        fixture.Http.Items["WebActivity.CommandId"] = command;
        Assert.Equal(RoleChangeResult.Updated, fixture.Developers.SetDeveloper("101", "104", true));
        Assert.False(fixture.Developers.IsDeveloper("104"));
        Assert.Equal(2, fixture.Read("Developer").PendingAuditEvents.Length);
        Assert.Equal(2, fixture.Read("Developer").ConsumedCommands!.Length);
    }

    [AccessRoleSqlFact]
    public void Admin_cannot_change_a_developer_and_SQL_revocation_is_immediately_observed()
    {
        using var fixture = new Fixture();
        fixture.Http.Session.SetString("UserCode", "102");
        Assert.Equal(RoleChangeResult.Forbidden, fixture.Admins.SetAdmin("102", "103", true));
        fixture.Http.Session.SetString("UserCode", "101");
        Assert.Equal(RoleChangeResult.Updated, fixture.SetDeveloper("103", false));
        var independent = new DeveloperRoleService(fixture.Configuration, NullLogger<DeveloperRoleService>.Instance);
        Assert.False(independent.IsDeveloper("103"));
        Assert.Equal(new[] { "101" }, independent.GetAllDevelopersForReports());
        Assert.Equal(RoleChangeResult.LastMember, fixture.SetDeveloper("101", false));
    }

    [AccessRoleSqlFact]
    public void SQL_projection_failure_rolls_back_membership_audit_and_receipt_together()
    {
        using var fixture = new Fixture();
        fixture.Execute("CREATE TRIGGER dbo.OcrFixtureRejectRoleMember ON dbo.tblApplicationRoleMember AFTER INSERT AS BEGIN IF EXISTS(SELECT 1 FROM inserted WHERE EmpCode=N'104') RAISERROR('Synthetic role projection failure',16,1); END");
        Assert.Throws<IOException>(() => fixture.SetDeveloper("104", true));
        Assert.False(fixture.Developers.IsDeveloper("104"));
        Assert.Empty(fixture.Read("Developer").PendingAuditEvents);
        Assert.Empty(fixture.Read("Developer").ConsumedCommands!);
        Assert.Equal(new[] { "101", "103" }, fixture.Developers.GetAllDevelopersForReports());
    }

    [AccessRoleSqlFact]
    public async Task Independent_services_serialize_writes_without_lost_members_or_receipts()
    {
        using var fixture = new Fixture();
        var otherHttp = Fixture.CreateHttp();
        var otherJournal = new RoleActivityJournal(fixture.Configuration,
            new WebActivityContextFactory(new Accessor { HttpContext = otherHttp }), TimeProvider.System,
            NullLogger<RoleActivityJournal>.Instance);
        var other = new DeveloperRoleService(fixture.Configuration, NullLogger<DeveloperRoleService>.Instance, otherJournal);
        var results = await Task.WhenAll(Task.Run(() => fixture.SetDeveloper("104", true)),
            Task.Run(() => other.SetDeveloper("101", "105", true)));
        Assert.All(results, result => Assert.Equal(RoleChangeResult.Updated, result));
        Assert.Equal(new[] { "101", "103", "104", "105" }, fixture.Developers.GetAllDevelopersForReports());
        Assert.Equal(2, fixture.Read("Developer").PendingAuditEvents.Length);
    }

    [AccessRoleSqlFact]
    public void Missing_or_mismatched_SQL_never_uses_the_still_present_JSON_membership()
    {
        using var fixture = new Fixture();
        fixture.Execute("DELETE dbo.tblApplicationRoleMember WHERE RoleName=N'Developer'");
        Assert.False(fixture.Developers.IsDeveloper("101"));
        Assert.Throws<IOException>(() => fixture.Developers.GetAllDevelopersForReports());
        fixture.Execute("DELETE dbo.tblApplicationRoleStore WHERE RoleName=N'Developer'");
        Assert.False(fixture.Developers.IsDeveloper("101"));
        Assert.Throws<IOException>(() => fixture.Developers.GetAllDevelopersForReports());
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "OcrSqlRoles_" + Guid.NewGuid().ToString("N"));
        private readonly string _connection;
        public string DeveloperPath => Path.Combine(_directory, "developers.json");
        private string AdminPath => Path.Combine(_directory, "admins.json");
        public IConfiguration Configuration { get; }
        public DefaultHttpContext Http { get; } = CreateHttp();
        public DeveloperRoleService Developers { get; }
        public AdminService Admins { get; }

        public Fixture()
        {
            var builder = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("OCR_ACCESS_ROLES_TEST_CONNECTION") ?? "");
            if (builder.DataSource is not ("(localdb)\\CodexSkillRecovery" or "(localdb)\\MSSQLLocalDB") ||
                !builder.InitialCatalog.StartsWith("OcrAccessRoleTests_", StringComparison.Ordinal))
                throw new InvalidOperationException("Only a marked local access-role SQL fixture is permitted.");
            _connection = builder.ConnectionString;
            using (var connection = new SqlConnection(_connection))
            {
                connection.Open();
                using var marker = new SqlCommand("SELECT CONVERT(nvarchar(50),value) FROM sys.extended_properties WHERE class=0 AND name=N'OcrEnvironment'", connection);
                if (!Equals(marker.ExecuteScalar(), "SyntheticLocalFixture")) throw new InvalidOperationException("The synthetic fixture marker is required.");
            }
            Execute("IF OBJECT_ID(N'dbo.tblApplicationRoleMember',N'U') IS NOT NULL DROP TABLE dbo.tblApplicationRoleMember; IF OBJECT_ID(N'dbo.tblApplicationRoleStore',N'U') IS NOT NULL DROP TABLE dbo.tblApplicationRoleStore;");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(DeveloperPath, "[\"101\",\"103\"]");
            File.WriteAllText(AdminPath, "[\"101\",\"102\"]");
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _connection, ["AccessRoles:Storage"] = "Sql",
                ["DeveloperRoles:StorePath"] = DeveloperPath, ["AdminRoles:StorePath"] = AdminPath
            }).Build();
            RoleActivitySqlMigration.Apply(Configuration, DeveloperPath, AdminPath);
            var journal = new RoleActivityJournal(Configuration,
                new WebActivityContextFactory(new Accessor { HttpContext = Http }), TimeProvider.System,
                NullLogger<RoleActivityJournal>.Instance);
            Developers = new DeveloperRoleService(Configuration, NullLogger<DeveloperRoleService>.Instance, journal);
            Admins = new AdminService(Configuration, developers: Developers, activity: journal);
        }

        public static DefaultHttpContext CreateHttp()
        {
            var http = new DefaultHttpContext();
            http.Features.Set<ISessionFeature>(new SessionFeature());
            http.Session.SetString("UserCode", "101");
            NewCommand(http);
            return http;
        }
        private static void NewCommand(HttpContext http)
        {
            http.Items["WebActivity.CommandId"] = Guid.NewGuid();
            http.Items["WebActivity.CommandFingerprint"] = new string('A', 64);
        }
        public RoleChangeResult SetDeveloper(string target, bool enabled)
        { NewCommand(Http); return Developers.SetDeveloper("101", target, enabled); }
        public RoleChangeResult SetAdmin(string target, bool enabled)
        { NewCommand(Http); return Admins.SetAdmin("101", target, enabled); }
        public RoleActivityEnvelope Read(string role)
        {
            var storage = new RoleActivityStorage(Configuration);
            return storage.Read(storage.Resolve(role));
        }
        public void Execute(string sql)
        {
            using var connection = new SqlConnection(_connection); connection.Open();
            using var command = new SqlCommand(sql, connection); command.ExecuteNonQuery();
        }
        public void Dispose()
        {
            foreach (var file in Directory.EnumerateFiles(_directory)) File.Delete(file);
            Directory.Delete(_directory);
        }
    }

    private sealed class Accessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = new TestSession(); }
}
