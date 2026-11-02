using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public partial class SqlEmployeeFileRepositoryTests
{
    private static string ConnectionString
    {
        get
        {
            var value =
                Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION")
                ?? throw new InvalidOperationException(
                    "SQL integration requires OCR_FILE_STORAGE_TEST_CONNECTION pointing to an isolated fixture database."
                );
            var b = new SqlConnectionStringBuilder(value);
            if (
                !System.Text.RegularExpressions.Regex.IsMatch(
                    b.InitialCatalog,
                    @"^OcrFileStorageTests_[A-Za-z0-9_]+$"
                )
                || !System.Text.RegularExpressions.Regex.IsMatch(
                    b.DataSource,
                    @"^(localhost|127\.0\.0\.1|\(local\)|\.)(,\d+|\\[A-Za-z0-9_]+)?$"
                )
            )
                throw new InvalidOperationException(
                    "SQL integration permits only a dedicated local OcrFileStorageTests_ database."
                );
            return value;
        }
    }

    [Fact]
    public async Task SqlEmployeeFile_AtomicWriteIntegrityOwnershipAndRollback()
    {
        var repo = new SqlEmployeeFileRepository(() => new SqlConnection(ConnectionString));
        Assert.True(await repo.IsSchemaAvailableAsync(CancellationToken.None));
        var employee = "F" + Guid.NewGuid().ToString("N").Substring(0, 9);
        using (var conn = new SqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            using (
                var cmd = new SqlCommand(
                    "INSERT dbo.tblEmployee(EmpCode,ResignDate,StatusWork) VALUES(@employee,NULL,N'1')",
                    conn
                )
            )
            {
                cmd.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                await cmd.ExecuteNonQueryAsync();
            }
        }
        var owner = new FileOwner(FileOwnerTable.Employee, employee);
        var state = await repo.GetOwnerStateAsync(owner, CancellationToken.None);
        var options = new FileStorageOptions(true, true, new[] { employee });
        var service = new EmployeeFileService(
            repo,
            () => new SqlConnection(ConnectionString),
            options
        );
        var bytes = Encoding.ASCII.GetBytes("GIF89a" + new string('a', 100));
        try
        {
            using (
                var staged = await service.StageAsync(
                    new MemoryStream(bytes),
                    employee,
                    FileKind.Photo,
                    "a.gif",
                    "fixture",
                    CancellationToken.None
                )
            )
            {
                var saved = await service.PublishAsync(
                    staged,
                    owner,
                    state.RowVersion,
                    async (connection, transaction, ct) =>
                    {
                        using (
                            var command = new SqlCommand(
                                "UPDATE dbo.tblEmployee SET Photo=N'old.gif' WHERE EmpCode=@employee",
                                connection,
                                transaction
                            )
                        )
                        {
                            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value =
                                employee;
                            await command.ExecuteNonQueryAsync(ct);
                        }
                    },
                    CancellationToken.None
                );
                using (
                    var lease = await repo.OpenReadAsync(
                        saved.FileId,
                        employee,
                        FileKind.Photo,
                        CancellationToken.None
                    )
                )
                using (var copy = new MemoryStream())
                {
                    await lease.Content.CopyToAsync(copy);
                    Assert.Equal(bytes, copy.ToArray());
                }
                await Assert.ThrowsAsync<FileNotFoundException>(() =>
                    repo.OpenReadAsync(
                        saved.FileId,
                        "0000000000",
                        FileKind.Photo,
                        CancellationToken.None
                    )
                );
                await Assert.ThrowsAsync<FileNotFoundException>(() =>
                    repo.OpenReadAsync(
                        saved.FileId,
                        employee,
                        FileKind.Certificate,
                        CancellationToken.None
                    )
                );
                var current = await repo.GetOwnerStateAsync(owner, CancellationToken.None);
                Assert.Equal(saved.FileId, current.FileId);
                using (var aliasConnection = new SqlConnection(ConnectionString))
                {
                    await aliasConnection.OpenAsync();
                    using (var aliasTransaction = aliasConnection.BeginTransaction())
                    {
                        await repo.RegisterAliasAsync(
                            aliasConnection,
                            aliasTransaction,
                            employee,
                            FileKind.Photo,
                            "Photos/Old.GIF",
                            saved.FileId,
                            CancellationToken.None
                        );
                        await repo.RegisterAliasAsync(
                            aliasConnection,
                            aliasTransaction,
                            employee,
                            FileKind.Photo,
                            "photos/old.gif",
                            saved.FileId,
                            CancellationToken.None
                        );
                        aliasTransaction.Commit();
                    }
                }
                Assert.Equal(
                    saved.FileId,
                    await repo.ResolveAliasAsync(
                        employee,
                        FileKind.Photo,
                        "photos/OLD.gif",
                        CancellationToken.None
                    )
                );
                Assert.Null(
                    await repo.ResolveAliasAsync(
                        "0000000000",
                        FileKind.Photo,
                        "photos/old.gif",
                        CancellationToken.None
                    )
                );
                Assert.Null(
                    await repo.ResolveAliasAsync(
                        employee,
                        FileKind.Certificate,
                        "photos/old.gif",
                        CancellationToken.None
                    )
                );
                staged.Content.Position = 0;
                await Assert.ThrowsAsync<DBConcurrencyException>(() =>
                    service.PublishAsync(
                        staged,
                        owner,
                        state.RowVersion,
                        null,
                        CancellationToken.None
                    )
                );
                staged.Content.Position = 0;
                await Assert.ThrowsAsync<ApplicationException>(() =>
                    service.PublishAsync(
                        staged,
                        owner,
                        current.RowVersion,
                        (c, t, ct) => throw new ApplicationException("business failure"),
                        CancellationToken.None
                    )
                );
                Assert.Equal(
                    saved.FileId,
                    (await repo.GetOwnerStateAsync(owner, CancellationToken.None)).FileId
                );
                using (var conn = new SqlConnection(ConnectionString))
                {
                    await conn.OpenAsync();
                    using (
                        var cmd = new SqlCommand(
                            "UPDATE dbo.tblEmployee SET ResignDate=GETUTCDATE() WHERE EmpCode=@employee",
                            conn
                        )
                    )
                    {
                        cmd.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                        await cmd.ExecuteNonQueryAsync();
                    }
                }
                using (
                    var lease = await repo.OpenReadAsync(
                        saved.FileId,
                        employee,
                        FileKind.Photo,
                        CancellationToken.None
                    )
                )
                using (var copy = new MemoryStream())
                    await lease.Content.CopyToAsync(copy);
                staged.Content.Position = 0;
                current = await repo.GetOwnerStateAsync(owner, CancellationToken.None);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    service.PublishAsync(
                        staged,
                        owner,
                        current.RowVersion,
                        null,
                        CancellationToken.None
                    )
                );
            }
        }
        finally
        {
            using (var conn = new SqlConnection(ConnectionString))
            {
                await conn.OpenAsync();
                using (
                    var cmd = new SqlCommand(
                        "UPDATE dbo.tblEmployee SET PhotoFileId=NULL WHERE EmpCode=@employee; DELETE dbo.tblEmployeeFileAlias WHERE EmpCode=@employee; DELETE dbo.tblEmployeeFile WHERE EmpCode=@employee; DELETE dbo.tblEmployee WHERE EmpCode=@employee",
                        conn
                    )
                )
                {
                    cmd.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
