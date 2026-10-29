using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class DeveloperRoleServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-developer-roles-" + Guid.NewGuid().ToString("N"));
    private string StorePath => Path.Combine(_directory, "developers.json");

    public DeveloperRoleServiceTests() => Directory.CreateDirectory(_directory);

    private DeveloperRoleService CreateService() => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["DeveloperRoles:StorePath"] = StorePath }).Build(), NullLogger<DeveloperRoleService>.Instance);

    private void Seed(string json = "[\"101\"]") => File.WriteAllText(StorePath, json);

    [Fact]
    public void ReportMembershipSnapshotIsSortedDistinctAndFreshWithoutChangingPreviousSnapshot()
    {
        Seed("[\"103\",\"00102\",\"101\",\"103\"]");
        var service = CreateService();
        var snapshot = service.GetAllDevelopersForReports();
        Assert.Equal(new[] { "00102", "101", "103" }, snapshot);
        Seed("[\"104\"]");
        Assert.Equal(new[] { "104" }, service.GetAllDevelopersForReports());
        Assert.Equal(new[] { "00102", "101", "103" }, snapshot);
        Seed("[]");
        Assert.Empty(service.GetAllDevelopersForReports());
    }

    [Fact]
    public void MissingReportMembershipStoreThrowsRatherThanReportingNoDevelopers()
    {
        Assert.ThrowsAny<IOException>(() => CreateService().GetAllDevelopersForReports());
        Assert.False(File.Exists(StorePath));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[\"101\",\"invalid\"]")]
    public void InvalidReportMembershipStoreThrowsAndDoesNotReturnPartialMembership(string contents)
    {
        Seed(contents);
        Assert.ThrowsAny<IOException>(() => CreateService().GetAllDevelopersForReports());
        Assert.Equal(contents, File.ReadAllText(StorePath));
    }

    [Fact]
    public void UnreadableReportMembershipStoreThrowsEvenAfterSuccessfulRead()
    {
        Seed();
        var service = CreateService();
        Assert.Equal(new[] { "101" }, service.GetAllDevelopersForReports());
        using (var heldStore = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => service.GetAllDevelopersForReports());
        Assert.Equal(new[] { "101" }, service.GetAllDevelopersForReports());
    }

    [Fact]
    public void DeveloperGateDoesNotInvokeMutationForNonDeveloper()
    {
        Seed();
        var invoked = false;
        var result = CreateService().ExecuteAsDeveloper("102", () =>
        {
            invoked = true;
            return RoleChangeResult.Updated;
        });
        Assert.Equal(RoleChangeResult.Forbidden, result);
        Assert.False(invoked);
    }

    [Fact]
    public async Task DeveloperGateSerializesMutationAndRevocationAcrossInstances()
    {
        Seed("[\"101\",\"102\"]");
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var revocationStarted = new ManualResetEventSlim();
        var mutation = Task.Run(() => CreateService().ExecuteAsDeveloper(" 101 ", () =>
        {
            entered.Set();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return RoleChangeResult.Updated;
        }));
        Task<RoleChangeResult>? revocation = null;
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            revocation = Task.Run(() =>
            {
                revocationStarted.Set();
                return CreateService().SetDeveloper("102", "101", false);
            });
            Assert.True(revocationStarted.Wait(TimeSpan.FromSeconds(5)));
            Assert.False(revocation.Wait(TimeSpan.FromMilliseconds(100)));
        }
        finally { release.Set(); }
        Assert.Equal(RoleChangeResult.Updated, await mutation);
        Assert.NotNull(revocation);
        Assert.Equal(RoleChangeResult.Updated, await revocation!);
        Assert.Equal(RoleChangeResult.Forbidden, CreateService().ExecuteAsDeveloper("101",
            () => throw new InvalidOperationException("Revoked developer must not run a mutation.")));
    }

    [Fact]
    public void DeveloperCanGrantAccessAndMembershipSurvivesNewServiceInstance()
    {
        Seed();
        var service = CreateService();
        Assert.Equal(RoleChangeResult.Updated, service.SetDeveloper("101", "102", true));
        Assert.True(service.IsDeveloper("102"));
        Assert.True(CreateService().IsDeveloper("102"));
        Assert.Equal(RoleChangeResult.Unchanged, service.SetDeveloper("101", "102", true));
    }

    [Fact]
    public void NormalUserCannotGrantOrRemoveDeveloperMembership()
    {
        Seed();
        var service = CreateService();
        Assert.Equal(RoleChangeResult.Forbidden, service.SetDeveloper("999", "999", true));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetDeveloper("999", "101", false));
        Assert.True(service.IsDeveloper("101"));
        Assert.False(service.IsDeveloper("999"));
    }

    [Fact]
    public void MutationRechecksFreshMembershipAndRevocationTakesEffectImmediately()
    {
        Seed("[\"101\",\"102\"]");
        var first = CreateService();
        var second = CreateService();
        Assert.True(first.IsDeveloper("101"));
        Assert.Equal(RoleChangeResult.Updated, second.SetDeveloper("102", "101", false));
        Assert.False(first.IsDeveloper("101"));
        Assert.Equal(RoleChangeResult.Forbidden, first.SetDeveloper("101", "103", true));
        Assert.False(second.IsDeveloper("103"));
    }

    [Fact]
    public void LastDeveloperCannotBeRemoved()
    {
        Seed();
        var service = CreateService();
        Assert.Equal(RoleChangeResult.LastMember, service.SetDeveloper("101", "101", false));
        Assert.True(service.IsDeveloper("101"));
    }

    [Fact]
    public void MissingStoreDoesNotBootstrapAnyPrivilegesOrCreateAFile()
    {
        var service = CreateService();
        Assert.False(service.IsDeveloper("101"));
        Assert.Equal(RoleChangeResult.Forbidden, service.SetDeveloper("101", "101", true));
        Assert.False(File.Exists(StorePath));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[\"101\",\"../102\"]")]
    [InlineData("{\"101\":true}")]
    public void CorruptStoreDeniesAccessAndCannotBeOverwritten(string json)
    {
        Seed(json);
        var service = CreateService();
        Assert.False(service.IsDeveloper("101"));
        Assert.ThrowsAny<IOException>(() => service.SetDeveloper("101", "102", true));
        Assert.Equal(json, File.ReadAllText(StorePath));
    }

    [Fact]
    public void FailedPersistenceDoesNotGrantAccessOrLeaveTemporaryFiles()
    {
        Seed();
        var service = CreateService();
        using (var heldStore = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var error = Record.Exception(() => service.SetDeveloper("101", "102", true));
            Assert.True(error is IOException or UnauthorizedAccessException,
                $"Expected a storage failure, received {error?.GetType().Name ?? "no exception"}.");
        }
        Assert.False(service.IsDeveloper("102"));
        Assert.True(service.IsDeveloper("101"));
        Assert.Single(Directory.GetFiles(_directory));
    }

    [Fact]
    public void UnreadableStoreDoesNotRetainPreviouslyConfirmedAccess()
    {
        Seed();
        var service = CreateService();
        Assert.True(service.IsDeveloper("101"));
        using (var heldStore = new FileStream(StorePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.False(service.IsDeveloper("101"));
            Assert.ThrowsAny<IOException>(() => service.SetDeveloper("101", "102", true));
        }
        Assert.True(service.IsDeveloper("101"));
        Assert.False(service.IsDeveloper("102"));
    }

    [Fact]
    public void LeadingZerosArePreservedAndWhitespaceIsNormalized()
    {
        Seed();
        var service = CreateService();
        Assert.Equal(RoleChangeResult.Updated, service.SetDeveloper(" 101 ", " 00102 ", true));
        Assert.True(service.IsDeveloper("00102"));
        Assert.False(service.IsDeveloper("102"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../102")]
    [InlineData("１２３")]
    [InlineData("123456789012345678901")]
    public void InvalidTargetCodesAreRejected(string code)
    {
        Seed();
        var service = CreateService();
        Assert.Throws<ArgumentException>(() => service.SetDeveloper("101", code, true));
        Assert.False(service.IsDeveloper(code));
    }

    public void Dispose()
    {
        foreach (var file in Directory.GetFiles(_directory)) File.Delete(file);
        Directory.Delete(_directory);
    }
}
