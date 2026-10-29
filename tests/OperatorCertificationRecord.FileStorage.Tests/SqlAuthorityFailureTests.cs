using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.FileStorage;
using Xunit;

public class SqlAuthorityFailureTests
{
    [Fact]
    public async Task SqlErrorCannotApproveLegacyFallback()
    {
        var repo = new SqlEmployeeFileRepository(() =>
            throw new IOException("synthetic unavailable SQL")
        );
        await Assert.ThrowsAsync<IOException>(() =>
            repo.AssertLegacyFallbackAllowedAsync(
                new FileOwner(FileOwnerTable.Employee, "00001"),
                CancellationToken.None
            )
        );
    }
}
