using OperatorCertificationRecord.FileMigration;
using Xunit;

public class InventoryOwnershipTests
{
    [Fact] public void IdenticalSourcesProduceOneVersion()
    {
        var rows = new[] { Candidate("a", "AA"), Candidate("b", "AA") };
        var result = InventoryCommand.Reconcile(rows);
        Assert.Single(result);
        Assert.Equal(Resolution.Ready, result[0].Resolution);
    }
    [Fact] public void DifferentBytesNeverChooseWinner()
    {
        var result = InventoryCommand.Reconcile(new[] { Candidate("a", "AA"), Candidate("b", "BB") });
        Assert.All(result, row => Assert.Equal(Resolution.SourceConflict, row.Resolution));
    }
    [Fact] public void ReusedPathNeverActivatesHistoricalOwner()
    {
        var result = InventoryCommand.Reconcile(new[] { Candidate("a", "AA"), Candidate("b", "AA") with { OwnerKey = Guid.NewGuid().ToString() } });
        Assert.All(result, row => Assert.Equal(Resolution.AmbiguousOwner, row.Resolution));
    }
    [Fact] public void LeadingZeroEmployeeCodeRemainsText() => Assert.Equal("00123", Candidate("a", "AA").EmployeeCode);
    [Theory][InlineData("../secret.pdf")][InlineData("x/../../secret.pdf")][InlineData("C:/secret.pdf")]
    public void UnsafeRelativeKeysRejected(string key) => Assert.Throws<InvalidDataException>(() => SourceFiles.CanonicalKey(key));
    private static ManifestEntry Candidate(string source, string sha) => new("00123", "Certificate", "00123/a.pdf", source, 10, sha, "Qualified", "11111111-1111-1111-1111-111111111111", "AAAAAAAAAAA=", null, true, Resolution.Ready);
}
