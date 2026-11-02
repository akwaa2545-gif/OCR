using System;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using OperatorTrainingRecord;
using Xunit;

public sealed class DesktopPhotoCommandTests
{
    [Fact] public void Transaction_clone_preserves_text_employee_identity_and_does_not_mutate_template()
    {
        using (var connection = new SqlConnection())
        using (var template = new SqlCommand("INSERT fixture VALUES(@sEmpCode,@sPhoto)"))
        {
            template.Parameters.Add("@sEmpCode", SqlDbType.NVarChar).Value = "001234";
            template.Parameters.Add("@sPhoto", SqlDbType.NVarChar).Value = "legacy.jpg";
            using (var sql = DesktopPhotoCommand.ForTransaction(template, connection, null, true))
            {
                Assert.Same(connection, sql.Connection);
                Assert.Equal("001234", sql.Parameters["@sEmpCode"].Value);
                Assert.Equal(10, sql.Parameters["@sEmpCode"].Size);
                Assert.Equal(DBNull.Value, sql.Parameters["@sPhoto"].Value);
                Assert.Equal("legacy.jpg", template.Parameters["@sPhoto"].Value);
            }
            using (var legacy = DesktopPhotoCommand.ForTransaction(template, connection, null, false))
                Assert.Equal("legacy.jpg", legacy.Parameters["@sPhoto"].Value);
        }
    }

    [Fact] public void Photo_update_binds_quoted_employee_and_path_as_data()
    {
        using (var connection = new SqlConnection())
        using (var command = DesktopPhotoCommand.ForLegacyPhotoUpdate("'OR'1'='1", "x';SELECT 1;--.jpg", connection, null))
        {
            Assert.DoesNotContain("'OR'", command.CommandText);
            Assert.Equal("'OR'1'='1", command.Parameters["@employeeCode"].Value);
            Assert.Equal(SqlDbType.NVarChar, command.Parameters["@employeeCode"].SqlDbType);
            Assert.Equal(10, command.Parameters["@employeeCode"].Size);
            Assert.Equal(50, command.Parameters["@legacyPhoto"].Size);
        }
    }

    [Fact] public void Legacy_photos_have_unique_owner_names_and_never_overwrite_existing_files()
    {
        var first = DesktopPhotoCommand.CreateLegacyPhotoPath(@"C:\p", "001234", ".JPG");
        var second = DesktopPhotoCommand.CreateLegacyPhotoPath(@"C:\p", "001234", ".jpg");
        Assert.NotEqual(first, second);
        Assert.Contains("001234-", first);
        Assert.EndsWith(".jpg", first);
        Assert.True(first.Length <= 50);
        var directory = Path.Combine(Path.GetTempPath(), "ocr-photo-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var source = Path.Combine(directory, "source.jpg");
        var output = Path.Combine(directory, "copy.jpg");
        File.WriteAllText(source, "original image fixture");
        try
        {
            DesktopPhotoCommand.CopyNewLegacyPhoto(source, output);
            File.WriteAllText(source, "replacement must not overwrite");
            Assert.Throws<IOException>(() => DesktopPhotoCommand.CopyNewLegacyPhoto(source, output));
            Assert.Equal("original image fixture", File.ReadAllText(output));
        }
        finally { File.Delete(source); File.Delete(output); Directory.Delete(directory); }
    }

    [Fact] public void Unsafe_or_oversized_legacy_names_are_rejected_before_copy()
    {
        Assert.Throws<ArgumentException>(() => DesktopPhotoCommand.CreateLegacyPhotoPath(@"C:\p", "../other", ".jpg"));
        Assert.Throws<ArgumentException>(() => DesktopPhotoCommand.CreateLegacyPhotoPath(@"C:\p", "001234", ".exe"));
        Assert.Throws<IOException>(() => DesktopPhotoCommand.CreateLegacyPhotoPath(@"C:\" + new string('a', 50), "001234", ".jpg"));
    }
}
