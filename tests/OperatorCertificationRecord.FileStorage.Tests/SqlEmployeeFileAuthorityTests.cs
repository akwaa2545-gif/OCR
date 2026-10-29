using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public partial class SqlEmployeeFileRepositoryTests
{
    [Fact]
    public async Task SqlEmployeeFile_MissingMarkerCannotDemoteReferencesAndOldSchemaRemainsReadable()
    {
        var builder = new SqlConnectionStringBuilder(ConnectionString);
        var database = "OcrFileStorageTests_Authority_" + Guid.NewGuid().ToString("N");
        builder.InitialCatalog = "master";
        using (var admin = new SqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            var created = false;
            try
            {
                using (var command = new SqlCommand("CREATE DATABASE [" + database + "]", admin))
                {
                    await command.ExecuteNonQueryAsync();
                    created = true;
                }
                using (
                    var compatibility = new SqlCommand(
                        "ALTER DATABASE [" + database + "] SET COMPATIBILITY_LEVEL=120",
                        admin
                    )
                )
                    await compatibility.ExecuteNonQueryAsync();
                builder.InitialCatalog = database;
                var connectionString = builder.ConnectionString;
                var repo = new SqlEmployeeFileRepository(() => new SqlConnection(connectionString));
                using (var fixture = new SqlConnection(connectionString))
                {
                    await fixture.OpenAsync();
                    using (
                        var command = new SqlCommand(
                            "CREATE TABLE dbo.tblEmployee(EmpCode nvarchar(10),Photo nvarchar(50)); CREATE TABLE dbo.tblQualified(EmpCode nvarchar(10),Download nvarchar(100)); CREATE TABLE dbo.tblQualified_Obsoleted(EmpCode nvarchar(10),Download nvarchar(100)); INSERT dbo.tblEmployee(EmpCode) VALUES(N'00001'); INSERT dbo.tblQualified(EmpCode) VALUES(N'00001');",
                            fixture
                        )
                    )
                        await command.ExecuteNonQueryAsync();
                    Assert.False(await repo.IsSchemaAvailableAsync(CancellationToken.None));
                    await repo.AssertLegacyFallbackAllowedAsync(
                        new FileOwner(FileOwnerTable.Employee, "00001"),
                        CancellationToken.None
                    );
                    await repo.AssertLegacyFallbackAllowedAsync(
                        "00001",
                        FileKind.Certificate,
                        null,
                        CancellationToken.None
                    );
                    using (
                        var command = new SqlCommand(
                            "ALTER TABLE dbo.tblEmployee ADD PhotoFileId uniqueidentifier NULL; ALTER TABLE dbo.tblQualified ADD CertificateFileId uniqueidentifier NULL; ALTER TABLE dbo.tblQualified_Obsoleted ADD CertificateFileId uniqueidentifier NULL; CREATE TABLE dbo.tblEmployeeFileStorageSchema(SchemaId tinyint PRIMARY KEY,SchemaVersion int); INSERT dbo.tblEmployeeFileStorageSchema VALUES(1,0);",
                            fixture
                        )
                    )
                        await command.ExecuteNonQueryAsync();
                    using (
                        var command = new SqlCommand(
                            "UPDATE dbo.tblEmployee SET PhotoFileId=NEWID(); UPDATE dbo.tblQualified SET CertificateFileId=NEWID();",
                            fixture
                        )
                    )
                        await command.ExecuteNonQueryAsync();
                    Assert.False(await repo.IsSchemaAvailableAsync(CancellationToken.None));
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repo.AssertLegacyFallbackAllowedAsync(
                            new FileOwner(FileOwnerTable.Employee, "00001"),
                            CancellationToken.None
                        )
                    );
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repo.AssertLegacyFallbackAllowedAsync(
                            "00001",
                            FileKind.Certificate,
                            null,
                            CancellationToken.None
                        )
                    );
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repo.AssertLegacyFallbackAllowedAsync(
                            "00001",
                            FileKind.Certificate,
                            Guid.NewGuid(),
                            CancellationToken.None
                        )
                    );
                    await repo.AssertLegacyFallbackAllowedAsync(
                        "00002",
                        FileKind.Certificate,
                        null,
                        CancellationToken.None
                    );
                    using (
                        var identity = new SqlCommand(
                            "ALTER TABLE dbo.tblQualified ADD OcrSkillRowId uniqueidentifier NULL",
                            fixture
                        )
                    )
                        await identity.ExecuteNonQueryAsync();
                    var stableRowId = Guid.NewGuid();
                    using (
                        var identity = new SqlCommand(
                            "UPDATE dbo.tblQualified SET OcrSkillRowId=@rowId",
                            fixture
                        )
                    )
                    {
                        identity
                            .Parameters.Add("@rowId", System.Data.SqlDbType.UniqueIdentifier)
                            .Value = stableRowId;
                        await identity.ExecuteNonQueryAsync();
                    }
                    await repo.AssertLegacyFallbackAllowedAsync(
                        "00001",
                        FileKind.Certificate,
                        Guid.NewGuid(),
                        CancellationToken.None
                    );
                    await Assert.ThrowsAsync<InvalidOperationException>(() =>
                        repo.AssertLegacyFallbackAllowedAsync(
                            new FileOwner(FileOwnerTable.Qualified, "00001", stableRowId),
                            CancellationToken.None
                        )
                    );
                }
            }
            finally
            {
                if (created)
                    using (
                        var cleanup = new SqlCommand(
                            "ALTER DATABASE ["
                                + database
                                + "] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ["
                                + database
                                + "]",
                            admin
                        )
                    )
                        await cleanup.ExecuteNonQueryAsync();
            }
        }
    }
}
