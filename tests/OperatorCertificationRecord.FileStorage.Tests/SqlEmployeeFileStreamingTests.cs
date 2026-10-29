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
    [Fact]
    public async Task SqlEmployeeFile_LargePdfIsStagedAndReadWithBoundedBuffers()
    {
        const long length = 61054694;
        var employee = "L" + Guid.NewGuid().ToString("N").Substring(0, 9);
        var repo = new SqlEmployeeFileRepository(() => new SqlConnection(ConnectionString));
        using (var connection = new SqlConnection(ConnectionString))
        {
            await connection.OpenAsync();
            using (
                var command = new SqlCommand(
                    "INSERT dbo.tblEmployee(EmpCode,ResignDate,StatusWork) VALUES(@employee,NULL,N'1')",
                    connection
                )
            )
            {
                command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                await command.ExecuteNonQueryAsync();
            }
            try
            {
                var source = new GeneratedPdfStream(length);
                using (
                    var staged = await FileStager.StageAsync(
                        source,
                        employee,
                        FileKind.Certificate,
                        "synthetic-large.pdf",
                        "fixture",
                        new FileStorageOptions(),
                        CancellationToken.None
                    )
                )
                {
                    StoredFileMetadata metadata;
                    using (var transaction = connection.BeginTransaction())
                    {
                        metadata = await repo.InsertVersionAsync(
                            connection,
                            transaction,
                            staged.Request,
                            staged.Content,
                            CancellationToken.None
                        );
                        transaction.Commit();
                    }
                    Assert.Equal(length, metadata.ByteLength);
                    Assert.InRange(source.MaximumReadRequest, 1, 81920);
                    using (
                        var lease = await repo.OpenReadAsync(
                            metadata.FileId,
                            employee,
                            FileKind.Certificate,
                            CancellationToken.None
                        )
                    )
                    {
                        Assert.False(lease.Content.CanSeek);
                        await lease.Content.CopyToAsync(Stream.Null);
                    }
                }
            }
            finally
            {
                using (
                    var command = new SqlCommand(
                        "DELETE dbo.tblEmployeeFile WHERE EmpCode=@employee; DELETE dbo.tblEmployee WHERE EmpCode=@employee",
                        connection
                    )
                )
                {
                    command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }

    private sealed class GeneratedPdfStream : Stream
    {
        private static readonly byte[] Header = Encoding.ASCII.GetBytes("%PDF-1.4\n");
        private readonly long length;
        private long position;

        public GeneratedPdfStream(long length)
        {
            this.length = length;
        }

        public int MaximumReadRequest { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            MaximumReadRequest = Math.Max(MaximumReadRequest, count);
            var read = (int)Math.Min(count, length - position);
            Array.Clear(buffer, offset, read);
            for (var i = 0; i < read && position + i < Header.Length; i++)
                buffer[offset + i] = Header[position + i];
            position += read;
            return read;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => position;
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
