using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopReportTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void Every_report_excludes_developers_using_typed_xml(int reportKind)
    {
        var kind = (DesktopReportKind)reportKind;
        using (var cmd = DesktopReportQuery.Create(kind, new[] { "00101", "00101", "2205529" }, new DateTime(2026, 1, 1), new DateTime(2026, 10, 30), "ไทย"))
        {
            Assert.Null(cmd.Connection);
            Assert.StartsWith("SELECT", cmd.CommandText.TrimStart());
            Assert.Contains("NOT EXISTS", cmd.CommandText);
            Assert.Equal(SqlDbType.Xml, cmd.Parameters["@ExcludedDeveloperEmployees"].SqlDbType);
            Assert.DoesNotContain("00101", cmd.CommandText);
            Assert.Equal(1, ((string)cmd.Parameters["@ExcludedDeveloperEmployees"].Value).Split(new[] { "<employee>00101</employee>" }, StringSplitOptions.None).Length - 1);
            Assert.Equal(new DateTime(2026, 10, 31), cmd.Parameters["@end"].Value);
            Assert.Equal(SqlDbType.DateTime, cmd.Parameters["@start"].SqlDbType);
        }
    }

    [Fact] public void Section_is_unicode_data_and_not_sql()
    {
        const string section = "ไทย';DELETE FROM tblEmployee;--";
        using (var cmd = DesktopReportQuery.Create(DesktopReportKind.Section, new string[0], null, null, section))
        {
            Assert.DoesNotContain(section, cmd.CommandText);
            Assert.Equal(section, cmd.Parameters["@SectName"].Value);
            Assert.Equal(SqlDbType.NVarChar, cmd.Parameters["@SectName"].SqlDbType);
            Assert.DoesNotContain("JobGrade IN", cmd.CommandText);
            Assert.DoesNotContain("'DAY'", cmd.CommandText);
        }
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Section, new string[0], null, null, " "));
    }

    [Fact] public void Report_populations_follow_the_web_exports()
    {
        using (var noSkill = DesktopReportQuery.Create(DesktopReportKind.NoSkill, new string[0], null, null))
        {
            Assert.Contains("SELECT DISTINCT q.EmpCode,q.JoinDate", noSkill.CommandText);
            Assert.Contains("q.Notice", noSkill.CommandText);
            Assert.Contains("q.DisQF = 0", noSkill.CommandText);
            Assert.Contains("q.ResignDate IS NULL", noSkill.CommandText);
            Assert.DoesNotContain("ResignBy", noSkill.CommandText);
            Assert.DoesNotContain("@start", noSkill.CommandText);
            Assert.DoesNotContain("CONVERT", noSkill.CommandText);
        }
        using (var obsolete = DesktopReportQuery.Create(DesktopReportKind.Obsoleted, new string[0], DateTime.Today, DateTime.Today))
        {
            Assert.Contains("FROM tblQualified_Obsoleted q", obsolete.CommandText);
            Assert.Contains("LEFT JOIN tblWorkshop w", obsolete.CommandText);
            Assert.Contains("e.JobGrade IN ('51T','52T')", obsolete.CommandText);
            Assert.DoesNotContain("q.ProcessName IS NOT NULL", obsolete.CommandText);
        }
        using (var resign = DesktopReportQuery.Create(DesktopReportKind.Resignation, new string[0], DateTime.Today, DateTime.Today))
        {
            Assert.Contains("q.ProcessName IS NOT NULL", resign.CommandText);
            Assert.DoesNotContain("JobGrade IN", resign.CommandText);
            Assert.DoesNotContain("ResignBy <> ", resign.CommandText);
        }
    }

    [Fact] public void Invalid_dates_and_developer_codes_fail_before_query()
    {
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Expiry, new string[0], null, null));
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.NoSkill, new string[0], DateTime.Today, null));
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Expiry, new string[0], DateTime.Today, DateTime.Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Expiry, new string[0], DateTime.Today, DateTime.MaxValue));
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Inventory, new[] { "101' OR 1=1" }, null, null));
        Assert.Throws<ArgumentException>(() => DesktopReportQuery.Create(DesktopReportKind.Inventory, new[] { "๑๐๑" }, null, null));
    }

    [Fact] public void Dates_and_employee_codes_keep_the_export_display_format()
    {
        Assert.Equal("30 Oct 2026", DesktopReportService.CellText(new DateTime(2026, 10, 30, 22, 0, 0)));
        Assert.Equal("0000101", DesktopReportService.CellText("0000101"));
        Assert.Equal("", DesktopReportService.CellText(DBNull.Value));
    }

    [Fact] public void Roles_support_legacy_and_case_insensitive_audited_envelopes_and_reread_changes()
    {
        WithFile("[\" 00101 \",\"00101\"]", path =>
        {
            Assert.Equal(new[] { "00101" }, DesktopDeveloperRoles.ReadRoles(path));
            File.WriteAllText(path, "{\"SchemaVersion\":1,\"MembershipRevision\":2,\"Members\":[\"2205529\"],\"PendingAuditEvents\":[],\"ConsumedCommands\":[]}");
            Assert.Equal(new[] { "2205529" }, DesktopDeveloperRoles.ReadRoles(path));
        });
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{members:[],schemaVersion:1,pendingAuditEvents:[]}")]
    [InlineData("['101']")]
    [InlineData("[\"101\",]")]
    [InlineData("/*comment*/[\"101\"]")]
    [InlineData("[101]")]
    [InlineData("[\"abc\"]")]
    [InlineData("{\"schemaVersion\":2,\"membershipRevision\":0,\"members\":[],\"pendingAuditEvents\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"membershipRevision\":-1,\"members\":[],\"pendingAuditEvents\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"membershipRevision\":null,\"members\":[],\"pendingAuditEvents\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"members\":[],\"pendingAuditEvents\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"members\":[],\"pendingAuditEvents\":[],\"consumedCommands\":[{}]}")]
    [InlineData("{\"schemaVersion\":1,\"members\":[]}")]
    public void Invalid_roles_fail_closed(string json) => WithFile(json, path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));

    [Fact] public void Audited_roles_validate_events_and_receipts_instead_of_ignoring_them()
    {
        string eventId = Guid.NewGuid().ToString();
        string operationId = Guid.NewGuid().ToString();
        string commandId = Guid.NewGuid().ToString();
        string audit = "{\"eventUid\":\"" + eventId + "\",\"originId\":\"" + eventId + "\",\"originKind\":\"RoleFile\",\"part\":\"0\",\"category\":\"roles\",\"action\":\"developer-enable\",\"outcome\":\"Committed\",\"actor\":{\"actorCode\":\"101\",\"isDeveloper\":true,\"operationId\":\"" + operationId + "\"},\"changes\":[{\"entityType\":\"access-role\",\"employeeCode\":\"202\",\"beforeExists\":true,\"afterExists\":true,\"fields\":[{\"field\":\"Developer\",\"before\":\"false\",\"after\":\"true\"}]}]}";
        string receipt = "{\"commandId\":\"" + commandId + "\",\"actorCode\":\"101\",\"targetCode\":\"202\",\"role\":\"Developer\",\"enabled\":true,\"intentHash\":\"" + new string('a', 64) + "\",\"outcome\":\"Updated\",\"expiresUtc\":\"2026-10-31T00:00:00+00:00\"}";
        string prefix = "{\"schemaVersion\":1,\"membershipRevision\":1,\"members\":[\"101\",\"202\"],\"pendingAuditEvents\":[";
        string valid = prefix + audit + "],\"consumedCommands\":[" + receipt + "]}";
        WithFile(valid, path => Assert.Equal(new[] { "101", "202" }, DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"isDeveloper\":true", "\"isDeveloper\":false"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"after\":\"true\"", "\"after\":\"false\""), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"outcome\":\"Updated\"", "\"outcome\":\"Bad\""), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("2026-10-31T00:00:00+00:00", "10/31/2026"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"enabled\":true", "\"enabled\":null"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"part\":\"0\"", "\"part\":null"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"originKind\":\"RoleFile\"", "\"originKind\":\"RoleFile\",\"occurredUtc\":null"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"field\":\"Developer\"", "\"field\":\"Developer\",\"redacted\":null"), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(valid.Replace("\"action\":\"developer-enable\"", "\"action\":\"admin-enable\"").Replace("\"isDeveloper\":true", "\"isAdmin\":true,\"isDeveloper\":\"bad\"").Replace("\"field\":\"Developer\"", "\"field\":\"Admin\""), path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(prefix + audit + "," + audit + "],\"consumedCommands\":[]}", path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(prefix + "],\"consumedCommands\":[" + receipt + "," + receipt + "]}", path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
    }

    [Fact] public void Role_metadata_limits_and_field_types_fail_closed()
    {
        string prefix = "{\"schemaVersion\":1,\"membershipRevision\":0,\"members\":[],\"pendingAuditEvents\":[";
        WithFile(prefix + string.Join(",", Enumerable.Repeat("{}", 1001)) + "]}", path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(prefix + "],\"consumedCommands\":[" + string.Join(",", Enumerable.Repeat("{}", 10001)) + "]}", path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
        WithFile(prefix.Replace("\"membershipRevision\":0", "\"membershipRevision\":\"0\"") + "]}", path => Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path)));
    }

    [Fact] public void Missing_oversized_and_non_utf8_roles_fail_closed()
    {
        Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(null));
        Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        WithFile("[]", path =>
        {
            File.WriteAllBytes(path, new byte[] { 0xff, 0xfe, 0x5b, 0x5d });
            Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path));
            File.WriteAllText(path, new string(' ', 5 * 1024 * 1024 + 1));
            Assert.Throws<IOException>(() => DesktopDeveloperRoles.ReadRoles(path));
        });
    }

    private static void WithFile(string json, Action<string> run)
    {
        var path = Path.GetTempFileName();
        try { File.WriteAllText(path, json, new UTF8Encoding(false)); run(path); }
        finally { File.Delete(path); }
    }
}
