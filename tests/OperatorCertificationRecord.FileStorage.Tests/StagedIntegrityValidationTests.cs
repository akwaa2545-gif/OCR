using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class StagedIntegrityValidationTests
{
    [Fact]
    public async Task RejectsChangedBytesBeforeAcquiringSqlLocks()
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.4 fixture");
        using (var sha = SHA256.Create())
        {
            var request = new FileWriteRequest(
                "00001",
                FileKind.Certificate,
                "a.pdf",
                "application/pdf",
                bytes.Length,
                sha.ComputeHash(bytes),
                "test"
            );
            bytes[6] = 99;
            var repo = new SqlEmployeeFileRepository(() =>
                throw new InvalidOperationException("SQL must not be opened")
            );
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                repo.InsertVersionAsync(
                    null!,
                    null!,
                    request,
                    new MemoryStream(bytes),
                    CancellationToken.None
                )
            );
        }
    }
}
