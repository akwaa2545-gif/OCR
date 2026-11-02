using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopSqlDeveloperRolesTests
{
    [Fact]
    public void Query_reads_the_shared_SQL_authority_and_keeps_an_empty_store_visible()
    {
        using (var command = DesktopSqlDeveloperRoles.CreateCommand())
        {
            Assert.Contains("dbo.tblApplicationRoleStore", command.CommandText);
            Assert.Contains("LEFT JOIN dbo.tblApplicationRoleMember", command.CommandText);
            Assert.DoesNotContain("NOLOCK", command.CommandText);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@RoleName"].SqlDbType);
            Assert.Equal(20, command.Parameters["@RoleName"].Size);
            Assert.Equal("Developer", command.Parameters["@RoleName"].Value);
            Assert.Null(command.Connection);
        }
    }

    [Fact]
    public void Missing_store_is_an_error_instead_of_an_empty_exclusion_list()
    {
        using (var rows = Roles())
        using (var reader = rows.CreateDataReader())
            Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadMembers(reader));
    }

    [Fact]
    public void Initialized_empty_store_is_valid()
    {
        using (var rows = Roles((object)DBNull.Value))
        using (var reader = rows.CreateDataReader())
            Assert.Empty(DesktopSqlDeveloperRoles.ReadMembers(reader));
    }

    [Fact]
    public void Codes_preserve_leading_zeroes_and_are_normalized_and_sorted()
    {
        using (var rows = Roles("2205529", " 00101 ", "00101", "101"))
        using (var reader = rows.CreateDataReader())
            Assert.Equal(new[] { "00101", "101", "2205529" }, DesktopSqlDeveloperRoles.ReadMembers(reader));
    }

    [Theory]
    [InlineData(101)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("101' OR 1=1")]
    [InlineData("๑๐๑")]
    [InlineData("123456789012345678901")]
    public void Malformed_SQL_membership_is_an_error(object value)
    {
        using (var rows = Roles(value))
        using (var reader = rows.CreateDataReader())
            Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadMembers(reader));
    }

    [Fact]
    public void Wrong_role_is_never_treated_as_Developer_membership()
    {
        using (var rows = Roles("101"))
        {
            rows.Rows[0]["RoleName"] = "Admin";
            using (var reader = rows.CreateDataReader())
                Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadMembers(reader));
        }
    }

    [Fact]
    public void Invalid_connection_settings_report_an_SQL_storage_error()
    {
        var error = Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadRoles("not a connection string"));
        Assert.Contains("SQL", error.Message);
        Assert.DoesNotContain("StorePath", error.Message);
    }

    [LocalRolesSqlFact]
    public void SQL_reader_distinguishes_missing_schema_missing_store_and_live_membership()
    {
        using (var database = new LocalRolesDatabase())
        {
            Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
            database.Execute(@"CREATE TABLE dbo.tblApplicationRoleStore
(RoleName nvarchar(20) NOT NULL PRIMARY KEY,Envelope nvarchar(max) NOT NULL);
CREATE TABLE dbo.tblApplicationRoleMember
(RoleName nvarchar(20) NOT NULL,EmpCode nvarchar(20) NOT NULL,
PRIMARY KEY(RoleName,EmpCode),FOREIGN KEY(RoleName) REFERENCES dbo.tblApplicationRoleStore(RoleName));");
            Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
            database.Execute(@"INSERT dbo.tblApplicationRoleStore VALUES(N'Developer',
N'{""schemaVersion"":1,""membershipRevision"":0,""members"":[],""pendingAuditEvents"":[],""consumedCommands"":[]}');");
            Assert.Empty(DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
            database.Execute(@"BEGIN TRANSACTION;
UPDATE dbo.tblApplicationRoleStore SET Envelope=N'{""schemaVersion"":1,""membershipRevision"":1,""members"": [""00101"",""2205529""],""pendingAuditEvents"":[],""consumedCommands"":[]}' WHERE RoleName=N'Developer';
INSERT dbo.tblApplicationRoleMember VALUES(N'Developer',N'00101'),(N'Developer',N'2205529');
COMMIT;");
            Assert.Equal(new[] { "00101", "2205529" }, DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
            database.Execute(@"BEGIN TRANSACTION;
UPDATE dbo.tblApplicationRoleStore SET Envelope=N'{""schemaVersion"":1,""membershipRevision"":2,""members"": [""2205529""],""pendingAuditEvents"":[],""consumedCommands"":[]}' WHERE RoleName=N'Developer';
DELETE dbo.tblApplicationRoleMember WHERE RoleName=N'Developer' AND EmpCode=N'00101';
COMMIT;");
            Assert.Equal(new[] { "2205529" }, DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
            database.Execute("INSERT dbo.tblApplicationRoleMember VALUES(N'Developer',N'invalid');");
            Assert.Throws<IOException>(() => DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
        }
    }

    private static DataTable Roles(params object[] codes)
    {
        var rows = new DataTable();
        rows.Columns.Add("RoleName", typeof(string));
        rows.Columns.Add("EmpCode", typeof(object));
        foreach (var code in codes) rows.Rows.Add("Developer", code);
        return rows;
    }

    [LocalRolesSqlFact]
    public async Task SQL_membership_read_and_atomic_replacement_cannot_interleave()
    {
        using (var database = new LocalRolesDatabase())
        using (var readerConnection = new SqlConnection(database.ConnectionString))
        using (var writerStarted = new ManualResetEventSlim())
        using (var replaced = new ManualResetEventSlim())
        using (var releaseWriter = new ManualResetEventSlim())
        {
            database.Execute(@"CREATE TABLE dbo.tblApplicationRoleStore
(RoleName nvarchar(20) NOT NULL PRIMARY KEY,Envelope nvarchar(max) NOT NULL);
CREATE TABLE dbo.tblApplicationRoleMember
(RoleName nvarchar(20) NOT NULL,EmpCode nvarchar(20) NOT NULL,
PRIMARY KEY(RoleName,EmpCode),FOREIGN KEY(RoleName) REFERENCES dbo.tblApplicationRoleStore(RoleName));
INSERT dbo.tblApplicationRoleStore VALUES(N'Developer',N'{}');
INSERT dbo.tblApplicationRoleMember VALUES(N'Developer',N'00101'),(N'Developer',N'2205529');");
            readerConnection.Open();
            using (var readTransaction = readerConnection.BeginTransaction(IsolationLevel.Serializable))
            {
                using (var command = DesktopSqlDeveloperRoles.CreateCommand())
                {
                    command.Connection = readerConnection;
                    command.Transaction = readTransaction;
                    using (var reader = command.ExecuteReader())
                        Assert.Equal(new[] { "00101", "2205529" }, DesktopSqlDeveloperRoles.ReadMembers(reader));
                }
                var writer = Task.Run(() => ReplaceMembers(database.ConnectionString, writerStarted, replaced, releaseWriter));
                try
                {
                    Assert.True(writerStarted.Wait(TimeSpan.FromSeconds(10)));
                    Assert.False(replaced.Wait(TimeSpan.FromMilliseconds(200)));
                    readTransaction.Commit();
                    Assert.True(replaced.Wait(TimeSpan.FromSeconds(10)));
                    var nextRead = Task.Run(() => DesktopSqlDeveloperRoles.ReadRoles(database.ConnectionString));
                    try
                    {
                        Assert.False(nextRead.Wait(TimeSpan.FromMilliseconds(200)));
                        releaseWriter.Set();
                        await writer;
                        Assert.Equal(new[] { "00303", "00404" }, await nextRead);
                    }
                    finally { releaseWriter.Set(); }
                }
                finally
                {
                    releaseWriter.Set();
                    if (readTransaction.Connection != null) readTransaction.Rollback();
                    await writer;
                }
            }
        }
    }

    private static void ReplaceMembers(string connectionString, ManualResetEventSlim started,
        ManualResetEventSlim replaced, ManualResetEventSlim release)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();
            using (var transaction = connection.BeginTransaction())
            using (var command = new SqlCommand(@"DELETE dbo.tblApplicationRoleMember WHERE RoleName=N'Developer';
INSERT dbo.tblApplicationRoleMember VALUES(N'Developer',N'00303'),(N'Developer',N'00404');", connection, transaction))
            {
                started.Set();
                command.ExecuteNonQuery();
                replaced.Set();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The replacement test was not released.");
                transaction.Commit();
            }
        }
    }

    private sealed class LocalRolesSqlFactAttribute : FactAttribute
    {
        public LocalRolesSqlFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("OCR_DESKTOP_ROLES_SQL_TESTS") != "1")
                Skip = "Set OCR_DESKTOP_ROLES_SQL_TESTS=1 to run the isolated LocalDB test.";
        }
    }

    private sealed class LocalRolesDatabase : IDisposable
    {
        private const string Master = @"Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=master;Integrated Security=True;Connect Timeout=10";
        private readonly string name = "OcrAccessRolesDesktopTests_" + Guid.NewGuid().ToString("N");
        public string ConnectionString { get; }

        internal LocalRolesDatabase()
        {
            ExecuteAt(Master, "CREATE DATABASE [" + name + "]");
            var builder = new SqlConnectionStringBuilder(Master) { InitialCatalog = name };
            ConnectionString = builder.ConnectionString;
        }

        internal void Execute(string sql) => ExecuteAt(ConnectionString, sql);

        private static void ExecuteAt(string connectionString, string sql)
        {
            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(sql, connection))
            {
                connection.Open();
                command.ExecuteNonQuery();
            }
        }

        public void Dispose()
        {
            using (var pool = new SqlConnection(ConnectionString)) SqlConnection.ClearPool(pool);
            ExecuteAt(Master, "ALTER DATABASE [" + name + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [" + name + "]");
        }
    }
}
