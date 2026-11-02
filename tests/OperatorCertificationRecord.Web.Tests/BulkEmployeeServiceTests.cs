using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class BulkEmployeeServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-bulk-" + Guid.NewGuid().ToString("N"));
    private readonly Store _store = new();
    private readonly Clock _clock = new();
    private readonly BulkEmployeeService _service;
    private string AdminPath => Path.Combine(_directory, "admins.json");
    private string DeveloperPath => Path.Combine(_directory, "developers.json");

    public BulkEmployeeServiceTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(AdminPath, "[\"101\",\"102\"]");
        File.WriteAllText(DeveloperPath, "[\"101\",\"103\"]");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["AdminRoles:StorePath"] = AdminPath, ["DeveloperRoles:StorePath"] = DeveloperPath }).Build();
        var developers = new DeveloperRoleService(configuration, NullLogger<DeveloperRoleService>.Instance);
        _service = new(_store, new AdminService(configuration), developers, new EphemeralDataProtectionProvider(),
            _clock, NullLogger<BulkEmployeeService>.Instance);
    }

    [Fact]
    public async Task PreviewPreservesLeadingZerosAndOnlyWhitelistedChanges()
    {
        var result = await Preview(["0012345"], new(Shift: " B "));
        Assert.True(result.Success);
        Assert.NotNull(result.Preview);
        Assert.Equal("0012345", result.Preview!.Batch.Rows.Single().EmpCode);
        Assert.Equal(new BulkEmployeePatch(Shift: "B"), result.Preview.Batch.Patch);
        Assert.Equal(0, _store.Writes);
    }

    [Theory]
    [InlineData("104")]
    [InlineData("")]
    public async Task NonAdminCannotPreviewOrReadEmployees(string actor)
    {
        Assert.False((await _service.PreviewAsync(actor, "session", ["0012345"], new(Shift: "B"), default)).Success);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.SearchAsync(actor, "", 1, default));
        Assert.Equal(0, _store.Reads);
    }

    [Fact]
    public async Task RequiresOneToOneHundredEmployeesAndAtLeastOneChange()
    {
        Assert.False((await Preview([], new(Shift: "B"))).Success);
        Assert.False((await Preview(Enumerable.Range(1, 101).Select(index => index.ToString()).ToArray(), new(Shift: "B"))).Success);
        Assert.False((await Preview(["0012345"], new())).Success);
        Assert.False((await Preview(["../bad"], new(Shift: "B"))).Success);
        Assert.Equal(0, _store.Reads);
    }

    [Theory]
    [InlineData("X")]
    [InlineData("A';DELETE")]
    public async Task ShiftMustBeARealChoice(string shift) => Assert.False((await Preview(["0012345"], new(Shift: shift))).Success);

    [Fact]
    public async Task LookupValuesAreValidatedAndAbsentEmployeeCannotDisappearFromPreview()
    {
        Assert.False((await Preview(["0012345"], new(WorkshopId: "unknown"))).Success);
        Assert.False((await Preview(["0012345"], new(JobGrade: "unknown"))).Success);
        Assert.False((await Preview(["0012345", "9999999"], new(Shift: "B"))).Success);
    }

    [Fact]
    public async Task PromotedGradeIsLockedEvenForDeveloperButShiftCanChange()
    {
        _store.Rows = [_store.Rows[0] with { Promoted = true }];
        Assert.False((await Preview(["0012345"], new(JobGrade: "53T"))).Success);
        Assert.True((await Preview(["0012345"], new(Shift: "B"))).Success);
    }

    [Fact]
    public async Task ResignedEmployeesCannotBeUpdatedInBulk()
    {
        _store.Rows = [_store.Rows[0] with { Current = false, ResignDate = DateTime.UtcNow }];
        Assert.False((await Preview(["0012345"], new(Shift: "B"))).Success);
    }

    [Fact]
    public async Task AdminOnlyCannotChangeDeveloperTarget()
    {
        _store.Rows = [_store.Rows[0] with { EmpCode = "103" }];
        Assert.False((await _service.PreviewAsync("102", "session", ["103"], new(Shift: "B"), default)).Success);
        Assert.True((await _service.PreviewAsync("101", "session", ["103"], new(Shift: "B"), default)).Success);
    }

    [Fact]
    public async Task PreviewTokenCannotBeTamperedOrUsedByDifferentActorOrSession()
    {
        var token = (await Preview(["0012345"], new(Shift: "B"))).Preview!.Token;
        Assert.False((await _service.ApplyAsync("101", "session", token[..^4] + "xxxx", true, default)).Success);
        Assert.False((await _service.ApplyAsync("102", "session", token, true, default)).Success);
        Assert.False((await _service.ApplyAsync("101", "other-session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ExpiredPreviewCannotApply()
    {
        var token = (await Preview(["0012345"], new(Shift: "B"))).Preview!.Token;
        _clock.Now = _clock.Now.AddMinutes(11);
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ConfirmationIsEnforcedServerSide()
    {
        var token = (await Preview(["0012345"], new(Shift: "B"))).Preview!.Token;
        Assert.False((await _service.ApplyAsync("101", "session", token, false, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task StaleRowRollsBackWholeSelection()
    {
        _store.Rows = [.. _store.Rows, _store.Rows[0] with { EmpCode = "0012346" }];
        var token = (await Preview(["0012345", "0012346"], new(Shift: "B"))).Preview!.Token;
        _store.Rows = [_store.Rows[0], _store.Rows[1] with { WorkshopId = "W02" }];
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal("A", _store.Rows[0].Shift);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task RevokedAdminOrNewDeveloperProtectionStopsApply()
    {
        var token = (await _service.PreviewAsync("102", "session", ["0012345"], new(Shift: "B"), default)).Preview!.Token;
        File.WriteAllText(DeveloperPath, "[\"101\",\"103\",\"0012345\"]");
        Assert.False((await _service.ApplyAsync("102", "session", token, true, default)).Success);
        File.WriteAllText(AdminPath, "[\"101\"]");
        Assert.False((await _service.ApplyAsync("102", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task AuthorityAndExpiryAreRecheckedBeforeCommit()
    {
        var token = (await Preview(["0012345"], new(Shift: "B"))).Preview!.Token;
        _store.BeforeCommit = () => File.WriteAllText(AdminPath, "[\"102\"]");
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
        File.WriteAllText(AdminPath, "[\"101\",\"102\"]");
        _store.BeforeCommit = () => _clock.Now = _clock.Now.AddMinutes(11);
        Assert.False((await _service.ApplyAsync("101", "session", token, true, default)).Success);
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task MissingRoleStoreFailsClosedWithoutWriting()
    {
        var token = (await Preview(["0012345"], new(Shift: "B"))).Preview!.Token;
        File.Delete(DeveloperPath);
        await Assert.ThrowsAnyAsync<IOException>(() => _service.ApplyAsync("101", "session", token, true, default));
        Assert.Equal(0, _store.Writes);
    }

    [Fact]
    public async Task ApplyUsesSnapshotPatchAndSecondApplyRejectsAlreadyChangedData()
    {
        var result = await Preview(["0012345"], new(WorkshopId: "W02", Shift: "B"));
        Assert.True((await _service.ApplyAsync("101", "session", result.Preview!.Token, true, default)).Success);
        Assert.Equal("W02", _store.Rows[0].WorkshopId); Assert.Equal("B", _store.Rows[0].Shift);
        Assert.Equal("52T", _store.Rows[0].JobGrade); Assert.Equal(1, _store.Writes);
        Assert.False((await _service.ApplyAsync("101", "session", result.Preview.Token, true, default)).Success);
        Assert.Equal(1, _store.Writes);
    }

    private Task<BulkEmployeeResult> Preview(string[] codes, BulkEmployeePatch patch) => _service.PreviewAsync("101", "session", codes, patch, default);
    public void Dispose() => Directory.Delete(_directory, true);
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class Store : IBulkEmployeeStore
    {
        public IReadOnlyList<BulkEmployeeRow> Rows { get; set; } = [new("0012345", "Synthetic Employee", "W01", "Inspection", "A", "52T", false, true)];
        public int Reads { get; private set; }
        public int Writes { get; private set; }
        public Action? BeforeCommit { get; set; }
        public Task<BulkEmployeeSearch> SearchAsync(string query, int page, CancellationToken ct) { Reads++; return Task.FromResult(new BulkEmployeeSearch(Rows, Rows.Count, page)); }
        public Task<BulkEmployeeChoices> ChoicesAsync(CancellationToken ct) => Task.FromResult(new BulkEmployeeChoices([new("W01", "Inspection"), new("W02", "Polymer")], [new("52T", "52T"), new("53T", "53T")]));
        public Task<IReadOnlyList<BulkEmployeeRow>> ReadSelectedAsync(IReadOnlyList<string> codes, CancellationToken ct)
        { Reads++; return Task.FromResult<IReadOnlyList<BulkEmployeeRow>>(Rows.Where(row => codes.Contains(row.EmpCode)).ToArray()); }
        public Task<int> ApplyAsync(BulkEmployeeBatch batch, Func<bool> authorized, CancellationToken ct)
        {
            if (!authorized()) throw new BulkEmployeeConflictException("Access changed.");
            var selected = Rows.Where(row => batch.Rows.Any(old => old.EmpCode == row.EmpCode)).ToArray();
            if (!selected.SequenceEqual(batch.Rows)) throw new BulkEmployeeConflictException("An employee changed.");
            var updated = Rows.Select(row => !selected.Contains(row) ? row : row with
            { WorkshopId = batch.Patch.WorkshopId ?? row.WorkshopId, Shift = batch.Patch.Shift ?? row.Shift, JobGrade = batch.Patch.JobGrade ?? row.JobGrade }).ToArray();
            BeforeCommit?.Invoke();
            if (!authorized()) throw new BulkEmployeeConflictException("Access changed before commit.");
            Rows = updated; Writes++; return Task.FromResult(selected.Length);
        }
    }
}
