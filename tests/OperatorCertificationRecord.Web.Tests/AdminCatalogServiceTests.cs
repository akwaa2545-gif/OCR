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

public sealed class AdminCatalogServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-catalog-" + Guid.NewGuid().ToString("N"));
    private readonly FakeStore _store = new();
    private readonly AdminCatalogService _service;
    private string AdminPath => Path.Combine(_directory, "admins.json");

    public AdminCatalogServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(AdminPath, "[\"101\",\"102\"]");
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["AdminRoles:StorePath"] = AdminPath }).Build();
        _service = new(_store, new AdminService(config), NullLogger<AdminCatalogService>.Instance);
    }

    [Theory]
    [InlineData("103")]
    [InlineData("")]
    public async Task NonAdminsCannotReachEitherCatalogMutation(string actor)
    {
        Assert.Equal(CatalogSaveStatus.Forbidden, (await _service.SaveWorkshopAsync(actor, Workshop())).Status);
        Assert.Equal(CatalogSaveStatus.Forbidden, (await _service.SaveProcessAsync(actor, Process())).Status);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Line\nBreak")]
    public async Task InvalidWorkshopInputNeverReachesStorage(string name)
    {
        var result = await _service.SaveWorkshopAsync("102", Workshop() with { NewWorkshopName = name });
        Assert.Equal(CatalogSaveStatus.Invalid, result.Status);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ValidWorkshopCreationNeedsOnlyTrimmedName()
    {
        var result = await _service.SaveWorkshopAsync(" 102 ", Workshop() with { NewWorkshopName = "  สายการผลิต  " });
        Assert.True(result.Success);
        Assert.Equal("สายการผลิต", _store.WorkshopInput!.NewWorkshopName);
        Assert.Equal("102", _store.Actor);
    }

    [Fact]
    public async Task LongNamesAreRejected()
    {
        Assert.Equal(CatalogSaveStatus.Invalid, (await _service.SaveWorkshopAsync("102", Workshop() with { NewWorkshopName = new string('x', 51) })).Status);
        Assert.Equal(CatalogSaveStatus.Invalid, (await _service.SaveProcessAsync("102", Process() with { NewProcessName = new string('x', 101) })).Status);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("unknown", "W01", "Process")]
    [InlineData("new", "", "Process")]
    [InlineData("new", "W01", " ")]
    public async Task InvalidProcessInputDoesNotWrite(string mode, string workshop, string name)
    {
        Assert.Equal(CatalogSaveStatus.Invalid, (await _service.SaveProcessAsync("102", Process() with { Mode = mode, DestinationWorkshopId = workshop, NewProcessName = name })).Status);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task MoveRequiresStableSelectionAndExplicitConfirmation()
    {
        var move = Process() with { Mode = "move", ExistingProcessKey = CatalogProcess.CreateKey("P001", "Process", "W01") };
        Assert.Equal(CatalogSaveStatus.Invalid, (await _service.SaveProcessAsync("102", move)).Status);
        Assert.Equal(CatalogSaveStatus.Invalid, (await _service.SaveProcessAsync("102", move with { ExistingProcessKey = "tampered", ConfirmMove = true })).Status);
        Assert.Equal(0, _store.Writes);
        Assert.True((await _service.SaveProcessAsync("102", move with { ConfirmMove = true })).Success);
        Assert.Equal(move.ExistingProcessKey, _store.ProcessInput!.ExistingProcessKey);
    }

    [Fact]
    public async Task PermissionIsRecheckedByStoreAuthorizationCallback()
    {
        _store.BeforeAuthorization = () => File.WriteAllText(AdminPath, "[\"101\"]");
        var result = await _service.SaveWorkshopAsync("102", Workshop());
        Assert.Equal(CatalogSaveStatus.Forbidden, result.Status);
    }

    [Fact]
    public async Task PersistenceFailureDoesNotReportSuccessOrExposeStorageDetails()
    {
        _store.Error = new IOException("private server storage details");
        var result = await _service.SaveProcessAsync("102", Process());
        Assert.Equal(CatalogSaveStatus.Unavailable, result.Status);
        Assert.DoesNotContain("private", result.Message);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        _store.Error = new OperationCanceledException(cancellation.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.SaveProcessAsync("102", Process(), cancellation.Token));
    }

    [Fact]
    public void StableProcessIdentityIncludesNullableIdNameAndSourceWorkshop()
    {
        var key = CatalogProcess.CreateKey(null, "Name", "W01");
        Assert.Equal(key, CatalogProcess.CreateKey(null, "Name", "W01"));
        Assert.NotEqual(key, CatalogProcess.CreateKey("", "Name", "W01"));
        Assert.NotEqual(key, CatalogProcess.CreateKey(null, "Name", "W02"));
        Assert.NotEqual(key, CatalogProcess.CreateKey(null, "Other", "W01"));
    }

    [Theory]
    [InlineData('W', 2, "W101")]
    [InlineData('P', 3, "P010")]
    public void IdAllocationUsesExistingNumericSuffixesWithoutReusingPaddedOrCaseVariants(char prefix, int width, string expected)
    {
        var ids = prefix == 'W' ? new string?[] { "W01", "w002", "W100", null, "custom" } : new string?[] { "P001", "p009", null };
        Assert.Equal(expected, SqlAdminCatalogStore.AllocateId(ids, prefix, width));
    }

    private static WorkshopCatalogInput Workshop() => new() { NewWorkshopName = "Workshop" };
    private static ProcessCatalogInput Process() => new() { Mode = "new", DestinationWorkshopId = "W01", NewProcessName = "Process" };

    private sealed class FakeStore : IAdminCatalogStore
    {
        public int Writes { get; private set; }
        public string? Actor { get; private set; }
        public WorkshopCatalogInput? WorkshopInput { get; private set; }
        public ProcessCatalogInput? ProcessInput { get; private set; }
        public Action? BeforeAuthorization { get; set; }
        public Exception? Error { get; set; }
        public Task<AdminCatalogSnapshot> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(AdminCatalogSnapshot.Empty);
        public Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken)
        {
            WorkshopInput = input;
            return Save(actor, authorized);
        }
        public Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken)
        {
            ProcessInput = input;
            return Save(actor, authorized);
        }
        private Task<CatalogSaveResult> Save(string actor, Func<bool> authorized)
        {
            Writes++; Actor = actor;
            if (Error != null) throw Error;
            BeforeAuthorization?.Invoke();
            return Task.FromResult(authorized() ? new CatalogSaveResult(CatalogSaveStatus.Saved, "Saved.", "W01") : new(CatalogSaveStatus.Forbidden, "Denied."));
        }
    }

    public void Dispose()
    {
        File.Delete(AdminPath);
        Directory.Delete(_directory);
    }
}
