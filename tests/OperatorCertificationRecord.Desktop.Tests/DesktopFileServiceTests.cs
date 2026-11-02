using System;
using System.Data.SqlClient;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.Security.Cryptography;
using System.Windows.Forms;
using OperatorCertificationRecord.FileStorage;
using OperatorTrainingRecord;
using Xunit;

public class DesktopFileServiceTests
{
    [Fact] public void Star_projection_already_contains_file_columns()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions());
        Assert.Equal("Select * From tblQualified", service.SkillProjection("Select * From tblQualified"));
    }

    [Fact] public void Joined_projection_qualifies_metadata_columns()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions());
        Assert.Contains("q.OcrSkillRowId,q.CertificateFileId,q.FileStorageVersion", service.SkillProjection("Select q.Download From tblQualified q LEFT JOIN tblEmployee v ON v.EmpCode=q.Verifier"));
    }

    [Fact] public void Schema_missing_with_default_flags_keeps_legacy_photo_write()
    {
        var service = new DesktopFileService(new FakeRepository { Schema = false }, () => throw new Exception(), new FileStorageOptions());
        Assert.False(service.TrySavePhoto("001234", "does-not-exist.jpg"));
    }

    [Fact] public void Changed_authoritative_certificate_does_not_fall_back_to_legacy()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions(true));
        Assert.Throws<System.Data.DBConcurrencyException>(() => service.CopyCertificate(new FileOwner(FileOwnerTable.Qualified, "001234", Guid.NewGuid()), "does-not-exist.pdf", "unused.pdf", Guid.NewGuid()));
    }

    [Fact] public void Sql_read_failure_never_tries_a_legacy_certificate()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions(true));
        Assert.Throws<InvalidDataException>(() => service.CopyCertificate(new FileOwner(FileOwnerTable.Obsolete, "001234", Guid.NewGuid()), "does-not-exist.pdf", "unused.pdf"));
    }
    [Fact] public void Old_schema_uses_legacy_without_requesting_new_columns()
    {
        var repo = new FakeRepository { Schema = false };
        var service = new DesktopFileService(repo, () => throw new Exception("No SQL write allowed"), new FileStorageOptions());
        Assert.Null(service.GetOwner(new FileOwner(FileOwnerTable.Employee, "001234")));
        Assert.Equal(0, repo.OwnerReads);
    }

    [Fact] public void Missing_marker_does_not_demote_an_authoritative_reference()
    {
        var service = new DesktopFileService(new FakeRepository { Schema = false }, () => throw new Exception(), new FileStorageOptions(),
            (employee, kind, row) => { throw new InvalidOperationException("SQL authority remains even when its marker is disabled."); });
        Assert.Throws<InvalidOperationException>(() => service.GetOwner(new FileOwner(FileOwnerTable.Employee, "001234")));
        Assert.Throws<InvalidOperationException>(() => service.SkillProjection("Select Download From tblQualified", "001234"));
    }

    [Fact] public void Authoritative_sql_photo_cannot_fall_back_when_reads_disabled()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions());
        Assert.Throws<InvalidOperationException>(() => service.CopyCertificate(new FileOwner(FileOwnerTable.Qualified, "001234", Guid.NewGuid()), "legacy.pdf", "unused.pdf"));
    }

    [Fact] public void Authoritative_photo_rejects_disabled_write_before_opening_source()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions());
        Assert.Throws<InvalidOperationException>(() => service.TrySavePhoto("001234", "does-not-exist.jpg"));
    }

    [Fact] public void Failed_photo_read_clears_the_previous_employees_picture()
    {
        var service = new DesktopFileService(new FakeRepository(), () => throw new Exception(), new FileStorageOptions(true));
        using (var target = new PictureBox { Image = new Bitmap(1, 1) })
        {
            Assert.Throws<InvalidDataException>(() => service.LoadPhoto(target, "001234", "never-read.jpg"));
            Assert.Null(target.Image);
        }
    }

    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    public void Sql_photo_is_decoded_and_cloned_before_lease_disposal(string format)
    {
        byte[] bytes;
        using (var bitmap = new Bitmap(2, 2))
        using (var output = new MemoryStream())
        {
            bitmap.SetPixel(0, 0, Color.Red);
            bitmap.Save(output, format == "gif" ? ImageFormat.Gif : ImageFormat.Bmp);
            bytes = output.ToArray();
        }
        var source = new MemoryStream(bytes);
        var repo = new FakeRepository { Lease = () => Lease(source, "photo." + format, "image/" + format, FileKind.Photo, bytes) };
        var service = new DesktopFileService(repo, () => throw new Exception(), new FileStorageOptions(true));
        using (var target = new PictureBox())
        {
            service.LoadPhoto(target, "001234", "never-read.jpg");
            Assert.False(source.CanRead);
            Assert.Equal(Color.Red.ToArgb(), ((Bitmap)target.Image).GetPixel(0, 0).ToArgb());
            target.Image.Dispose();
        }
    }

    [Fact] public void Corrupt_sql_certificate_does_not_overwrite_selected_destination()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocr-desktop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, "saved.pdf");
        File.WriteAllText(destination, "existing destination");
        var expected = new byte[] { 1, 2, 3 };
        var repo = new FakeRepository { Lease = () => Lease(new MemoryStream(new byte[] { 1, 2, 4 }), "test.pdf", "application/pdf", FileKind.Certificate, expected) };
        try
        {
            var service = new DesktopFileService(repo, () => throw new Exception(), new FileStorageOptions(true));
            Assert.Throws<InvalidDataException>(() => service.CopyCertificate(new FileOwner(FileOwnerTable.Qualified, "001234", Guid.NewGuid()), "never-read.pdf", destination));
            Assert.Equal("existing destination", File.ReadAllText(destination));
        }
        finally { File.Delete(destination); Directory.Delete(directory); }
    }

    [Fact] public void Large_certificate_is_streamed_to_disk_without_loading_a_byte_array()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ocr-desktop-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "source.pdf");
        var output = Path.Combine(directory, "result.pdf");
        const long length = 61L * 1024 * 1024;
        using (var fixture = File.Create(input)) { fixture.Write(new byte[] { 37, 80, 68, 70, 45 }, 0, 5); fixture.SetLength(length); }
        byte[] digest;
        using (var fixture = File.OpenRead(input)) using (var hash = SHA256.Create()) digest = hash.ComputeHash(fixture);
        var repo = new FakeRepository { Lease = () => new StoredFileLease(new StoredFileMetadata(Guid.NewGuid(), new FileWriteRequest("001234", FileKind.Certificate, "source.pdf", "application/pdf", length, digest, "test"), DateTime.UtcNow), File.OpenRead(input), new MemoryStream()) };
        try
        {
            new DesktopFileService(repo, () => throw new Exception(), new FileStorageOptions(true)).CopyCertificate(new FileOwner(FileOwnerTable.Obsolete, "001234", Guid.NewGuid()), "never-read.pdf", output);
            Assert.Equal(length, new FileInfo(output).Length);
            using (var actual = File.OpenRead(output)) using (var hash = SHA256.Create()) Assert.Equal(digest, hash.ComputeHash(actual));
        }
        finally { File.Delete(input); File.Delete(output); Directory.Delete(directory); }
    }

    private static StoredFileLease Lease(Stream content, string name, string mime, FileKind kind, byte[] expected)
    {
        byte[] digest;
        using (var hash = SHA256.Create()) digest = hash.ComputeHash(expected);
        return new StoredFileLease(new StoredFileMetadata(Guid.NewGuid(), new FileWriteRequest("001234", kind, name, mime, expected.Length, digest, "test"), DateTime.UtcNow), content, new MemoryStream());
    }

    private sealed class FakeRepository : IEmployeeFileRepository
    {
        public bool Schema = true; public int OwnerReads;
        public Func<StoredFileLease> Lease;
        public Task<bool> IsSchemaAvailableAsync(CancellationToken ct) => Task.FromResult(Schema);
        public Task<FileOwnerState> GetOwnerStateAsync(FileOwner owner, CancellationToken ct) { OwnerReads++; return Task.FromResult(new FileOwnerState(owner, Guid.NewGuid(), new byte[8], "legacy.pdf", DateTime.UtcNow, "0")); }
        public Task<StoredFileLease> OpenReadAsync(Guid id,string emp,FileKind kind,CancellationToken ct) => Lease == null ? throw new InvalidDataException("corrupt SQL file") : Task.FromResult(Lease());
        public Task<StoredFileMetadata> InsertVersionAsync(SqlConnection c,SqlTransaction t,FileWriteRequest r,Stream s,CancellationToken ct) => throw new NotImplementedException();
        public Task LinkOwnerAsync(SqlConnection c,SqlTransaction t,FileOwner o,Guid id,byte[] v,CancellationToken ct) => throw new NotImplementedException();
        public Task<Guid?> ResolveAliasAsync(string e,FileKind k,string key,CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task RegisterAliasAsync(SqlConnection c,SqlTransaction t,string e,FileKind k,string key,Guid id,CancellationToken ct) => throw new NotImplementedException();
    }
}
