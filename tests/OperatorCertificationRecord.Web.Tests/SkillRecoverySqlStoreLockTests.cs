using System;
using System.Data;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed partial class SkillRecoverySqlStoreTests
{
    [SkillRecoverySqlFact]
    public async Task OriginalUndoAddGuardAllowsAuditedArchiveCleanupAndBlocksCompetingReversalUntilReleased()
    {
        using var fixture = await Fixture.CreateAsync();
        var selected = await fixture.SeedAsync(Source, "A", file: true);
        var original = await fixture.PlanAsync(new() { Action = "undo-add", EmployeeCode = Source, SourceRowId = selected });
        await fixture.Store.ApplyAsync(original, () => true, default);
        var archive = await fixture.SeedAsync(Source, "A", archive: true, file: true);
        using var guardConnection = Connection();
        await guardConnection.OpenAsync();
        using var guard = guardConnection.BeginTransaction(IsolationLevel.Serializable);
        using (var command = new SqlCommand("SELECT a.PlanJson FROM dbo.tblSkillRecoveryAction a WITH (HOLDLOCK) WHERE a.ActionId=@action AND a.Operation=N'undo-add' AND a.SourceEmployee=@employee AND NOT EXISTS(SELECT 1 FROM dbo.tblSkillRecoveryAction u WITH (HOLDLOCK) WHERE u.ReversesActionId=a.ActionId)", guardConnection, guard))
        {
            command.CommandTimeout = 5;
            command.Parameters.AddWithValue("@action", original.ActionId);
            command.Parameters.AddWithValue("@employee", Source);
            Assert.IsType<string>(await command.ExecuteScalarAsync());
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var preview = await fixture.Service.PreviewAsync("101", "fixture", new()
        {
            Action = "undo-add", EmployeeCode = Source, SourceTable = SkillRecoveryTable.Archive,
            SourceRowId = archive, Reason = "Synthetic prior Undo Add cleanup"
        }, deadline.Token);
        Assert.True(preview.Success, preview.Message);
        var cleanup = await fixture.Service.ApplyAsync("101", "fixture", preview.Preview!.Token, true, deadline.Token);
        Assert.True(cleanup.Success, cleanup.Message);

        using var competingConnection = Connection();
        await competingConnection.OpenAsync();
        using var reversal = new SqlCommand("SET LOCK_TIMEOUT 200; INSERT dbo.tblSkillRecoveryAction(ActionId,Operation,Actor,Reason,CreatedUtc,SourceEmployee,TargetEmployee,SourceRowId,CreatedRowId,ReversesActionId,BeforeJson,AfterJson,PlanJson) SELECT @reversal,N'undo',Actor,N'Synthetic competing reversal',SYSUTCDATETIME(),SourceEmployee,TargetEmployee,SourceRowId,NULL,ActionId,BeforeJson,AfterJson,PlanJson FROM dbo.tblSkillRecoveryAction WHERE ActionId=@action", competingConnection);
        reversal.CommandTimeout = 5;
        reversal.Parameters.AddWithValue("@reversal", Guid.NewGuid());
        reversal.Parameters.AddWithValue("@action", original.ActionId);
        var blocked = await Assert.ThrowsAsync<SqlException>(() => reversal.ExecuteNonQueryAsync());
        Assert.Equal(1222, blocked.Number);
        guard.Commit();
        Assert.Equal(1, await reversal.ExecuteNonQueryAsync());
        Assert.Equal(0, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblQualified_Obsoleted"));
        Assert.Equal(3, await fixture.ScalarAsync("SELECT COUNT(*) FROM dbo.tblSkillRecoveryAction"));
    }
}
