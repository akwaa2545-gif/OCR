using System;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using OperatorCertificationRecord.FileStorage;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopSqlFixtureTests
{
    [LocalSqlFact]
    [Trait("Category", "SqlIntegration")]
    public void Certificate_add_replace_archive_and_resigned_read_use_the_same_file_contract()
    {
        var connectionString = LocalFixtureConnection();
        var employee = "D" + Guid.NewGuid().ToString("N").Substring(0, 9);
        var directory = Path.Combine(Path.GetTempPath(), "ocr-desktop-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "input.pdf");
        var saved = Path.Combine(directory, "saved.pdf");
        var firstContent = "%PDF-1.4\n% original fixture certificate\n%%EOF";
        var replacementContent = "%PDF-1.4\n% replacement fixture certificate\n%%EOF";
        var repository = new SqlEmployeeFileRepository(() => new SqlConnection(connectionString));
        var service = new DesktopFileService(repository, () => new SqlConnection(connectionString), new FileStorageOptions(true, true, new[] { employee }));
        Guid firstRow = Guid.Empty, replacementRow = Guid.Empty;
        try
        {
            using (var connection = new SqlConnection(connectionString)) { connection.Open(); InsertEmployee(connection, null, employee); }
            File.WriteAllText(source, firstContent, new System.Text.UTF8Encoding(false));
            Assert.True(service.TrySaveCertificate(employee, null, source, (c, t, row) =>
            {
                firstRow = row;
                using (var command = new SqlCommand("INSERT dbo.tblQualified(EmpCode,OcrSkillRowId,ProcessName,Download) VALUES(@employee,@row,N'DesktopFixture',N'legacy-certificate.pdf')", c, t))
                {
                    command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                    command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = row;
                    command.ExecuteNonQuery();
                }
            }));
            var first = service.GetOwner(new FileOwner(FileOwnerTable.Qualified, employee, firstRow));
            File.WriteAllText(source, replacementContent, new System.Text.UTF8Encoding(false));
            Assert.True(service.TrySaveCertificate(employee, firstRow, source, (c, t, row) =>
            {
                replacementRow = row;
                DesktopSkillMutation.Replace(c, t, employee, firstRow, row, first.RowVersion, new Dictionary<string, object> { ["OperatorTraining"] = "fixture replacement", ["CertifiedDate"] = DateTime.Today });
            }));
            var currentOwner = new FileOwner(FileOwnerTable.Qualified, employee, replacementRow);
            var archiveOwner = new FileOwner(FileOwnerTable.Obsolete, employee, firstRow);
            var current = service.GetOwner(currentOwner);
            var archived = service.GetOwner(archiveOwner);
            Assert.Equal(first.FileId, archived.FileId);
            Assert.NotEqual(archived.FileId, current.FileId);
            Assert.Equal("legacy-certificate.pdf", current.LegacyPath);
            var legacyCallbackRan = false;
            Assert.Throws<InvalidOperationException>(() => service.ExecuteLegacyMutation(employee, FileKind.Certificate, replacementRow, (c, t) => legacyCallbackRan = true));
            Assert.False(legacyCallbackRan);
            service.CopyCertificate(currentOwner, "never-read.pdf", saved, current.FileId);
            Assert.Equal(replacementContent, File.ReadAllText(saved));
            service.CopyCertificate(archiveOwner, "never-read.pdf", saved, archived.FileId);
            Assert.Equal(firstContent, File.ReadAllText(saved));
            Assert.Throws<InvalidOperationException>(() => service.TrySaveCertificate(employee, replacementRow, source, (c, t, row) =>
            {
                DesktopSkillMutation.Replace(c, t, employee, replacementRow, row, current.RowVersion, new Dictionary<string, object> { ["Remark"] = "must roll back" });
                throw new InvalidOperationException("Fixture transaction rollback");
            }));
            Assert.Equal(current.FileId, service.GetOwner(currentOwner).FileId);
            var legacyRow = Guid.NewGuid();
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("INSERT dbo.tblQualified(EmpCode,OcrSkillRowId,ProcessName,Download) VALUES(@employee,@row,N'DesktopFixture',N'legacy-only.pdf')", connection))
                {
                    command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                    command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = legacyRow;
                    command.ExecuteNonQuery();
                }
            }
            var legacyState = service.GetOwner(new FileOwner(FileOwnerTable.Qualified, employee, legacyRow));
            service.ExecuteLegacyMutation(employee, FileKind.Certificate, legacyRow, (c, t) =>
                DesktopSkillMutation.UpdateLegacy(c, t, employee, "DesktopFixture", legacyRow, legacyState.RowVersion, true,
                    new Dictionary<string, object> { ["Remark"] = "selected legacy row only" }, "legacy-updated.pdf"));
            Assert.Equal(current.FileId, service.GetOwner(currentOwner).FileId);
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("UPDATE dbo.tblEmployee SET ResignDate=GETDATE(),StatusWork=N'0' WHERE EmpCode=@employee", connection))
                { command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee; command.ExecuteNonQuery(); }
            }
            service.CopyCertificate(archiveOwner, "never-read.pdf", saved, archived.FileId);
            Assert.Equal(firstContent, File.ReadAllText(saved));
            Assert.Throws<InvalidOperationException>(() => service.TrySaveCertificate(employee, replacementRow, source, (c, t, row) => { }));
        }
        finally
        {
            File.Delete(source); File.Delete(saved); Directory.Delete(directory);
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("DELETE dbo.tblQualified WHERE EmpCode=@employee; DELETE dbo.tblQualified_Obsoleted WHERE EmpCode=@employee; UPDATE dbo.tblEmployee SET PhotoFileId=NULL WHERE EmpCode=@employee; DELETE dbo.tblEmployeeFile WHERE EmpCode=@employee; DELETE dbo.tblEmployee WHERE EmpCode=@employee", connection))
                { command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee; command.ExecuteNonQuery(); }
            }
        }
    }

    [LocalSqlFact]
    [Trait("Category", "SqlIntegration")]
    public void New_employee_and_photo_commit_together_and_failed_mutation_rolls_back()
    {
        var connectionString = LocalFixtureConnection();
        var employee = "D" + Guid.NewGuid().ToString("N").Substring(0, 9);
        var rollbackEmployee = "D" + Guid.NewGuid().ToString("N").Substring(0, 9);
        var legacyEmployee = "L" + Guid.NewGuid().ToString("N").Substring(0, 9);
        var path = Path.Combine(Path.GetTempPath(), "ocr-desktop-fixture-" + Guid.NewGuid().ToString("N") + ".bmp");
        using (var bitmap = new Bitmap(1, 1)) bitmap.Save(path, ImageFormat.Bmp);
        var repository = new SqlEmployeeFileRepository(() => new SqlConnection(connectionString));
        var service = new DesktopFileService(repository, () => new SqlConnection(connectionString), new FileStorageOptions(true, true, new[] { employee, rollbackEmployee }));
        try
        {
            Assert.True(service.TrySavePhoto(employee, path, (c, t) => InsertEmployee(c, t, employee), true));
            var firstPhoto = service.GetOwner(new FileOwner(FileOwnerTable.Employee, employee)).FileId;
            Assert.NotNull(firstPhoto);
            var photoMutationRan = false;
            Assert.True(service.TrySavePhoto(employee, path, (c, t) =>
            {
                using (var command = new SqlCommand("UPDATE dbo.tblEmployee SET Photo=Photo WHERE EmpCode=@employee", c, t))
                { command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee; command.ExecuteNonQuery(); }
                photoMutationRan = true;
            }));
            Assert.True(photoMutationRan);
            Assert.NotEqual(firstPhoto, service.GetOwner(new FileOwner(FileOwnerTable.Employee, employee)).FileId);
            var callbackRan = false;
            Assert.Throws<InvalidOperationException>(() => service.ExecuteLegacyMutation(employee, FileKind.Photo, null, (c, t) => callbackRan = true));
            Assert.False(callbackRan);
            using (var connection = new SqlConnection(connectionString)) { connection.Open(); InsertEmployee(connection, null, legacyEmployee); }
            Assert.Throws<InvalidOperationException>(() => service.ExecuteLegacyMutation(legacyEmployee, FileKind.Photo, null, (c, t) =>
            {
                using (var command = new SqlCommand("UPDATE dbo.tblEmployee SET Photo=N'rollback.jpg' WHERE EmpCode=@employee", c, t))
                { command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = legacyEmployee; command.ExecuteNonQuery(); }
                throw new InvalidOperationException("Fixture legacy rollback");
            }));
            Assert.Null(service.GetOwner(new FileOwner(FileOwnerTable.Employee, legacyEmployee)).LegacyPath);
            Assert.Throws<InvalidOperationException>(() => service.ExecuteLegacyMutation(rollbackEmployee, FileKind.Photo, null, (c, t) =>
            {
                InsertEmployee(c, t, rollbackEmployee);
                throw new InvalidOperationException("Fixture legacy new-employee rollback");
            }));
            Assert.Throws<InvalidOperationException>(() => service.TrySavePhoto(rollbackEmployee, path, (c, t) => { InsertEmployee(c, t, rollbackEmployee); throw new InvalidOperationException("Fixture rollback"); }, true));
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("SELECT COUNT(*) FROM dbo.tblEmployee WHERE EmpCode=@employee", connection))
                {
                    command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = rollbackEmployee;
                    Assert.Equal(0, Convert.ToInt32(command.ExecuteScalar()));
                }
            }
        }
        finally
        {
            File.Delete(path);
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                using (var command = new SqlCommand("UPDATE dbo.tblEmployee SET PhotoFileId=NULL WHERE EmpCode IN (@employee,@rollback,@legacy); DELETE dbo.tblEmployeeFile WHERE EmpCode IN (@employee,@rollback,@legacy); DELETE dbo.tblEmployee WHERE EmpCode IN (@employee,@rollback,@legacy)", connection))
                {
                    command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                    command.Parameters.Add("@rollback", SqlDbType.NVarChar, 10).Value = rollbackEmployee;
                    command.Parameters.Add("@legacy", SqlDbType.NVarChar, 10).Value = legacyEmployee;
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    private static void InsertEmployee(SqlConnection connection, SqlTransaction transaction, string employee)
    {
        using (var command = new SqlCommand("INSERT dbo.tblEmployee(EmpCode,ResignDate,StatusWork) VALUES(@employee,NULL,N'1')", connection, transaction))
        {
            command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
            command.ExecuteNonQuery();
        }
    }

    private static string LocalFixtureConnection()
    {
        var value = Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION");
        var parsed = new SqlConnectionStringBuilder(value);
        if (!Regex.IsMatch(parsed.InitialCatalog, @"^OcrFileStorageTests_[A-Za-z0-9_]+$") || !Regex.IsMatch(parsed.DataSource, @"^(localhost|127\.0\.0\.1|\(local\)|\.)(,\d+|\\[A-Za-z0-9_]+)?$"))
            throw new InvalidOperationException("Desktop SQL fixtures require a dedicated local OcrFileStorageTests_ database.");
        return value;
    }

    private sealed class LocalSqlFactAttribute : FactAttribute
    {
        public LocalSqlFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OCR_FILE_STORAGE_TEST_CONNECTION")))
                Skip = "An isolated local SQL fixture was not supplied.";
        }
    }
}
