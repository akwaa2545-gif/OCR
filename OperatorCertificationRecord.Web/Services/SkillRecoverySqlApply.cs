using System.Data;
using System.Data.SqlClient;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed partial class SkillRecoverySqlStore
{
    public async Task<SkillRecoveryApplyResult> ApplyAsync(SkillRecoveryPlan plan, Func<bool> authorized, CancellationToken ct)
    {
        if (!authorized()) throw Conflict("Developer access or the preview expired. Nothing was changed.");
        var validation = SkillRecoveryRules.Validate(plan.Request, plan.Context);
        if (validation != null) throw Conflict(validation);
        if (SkillRecoveryRules.IsPdfAction(plan.Request.Action) ||
            plan.Request.Action == "undo" && SkillRecoveryRules.IsPdfAction(plan.Context.OriginalAction))
            EnsureAttachmentWrites(plan.Context.SourceEmployee.EmployeeCode);
        using var replacement = await AcquireReplacementAsync(plan, ct);
        if (replacement != null && (replacement.Stage.Request.ByteLength > options.MaxCertificateBytes ||
            replacement.Stage.Request.EmployeeCode != plan.Context.SourceEmployee.EmployeeCode ||
            replacement.Stage.Request.Kind != FileKind.Certificate || replacement.Info.ByteLength != replacement.Stage.Request.ByteLength ||
            replacement.Info.Sha256 != Convert.ToHexString(replacement.Stage.Request.Sha256)))
            throw Conflict("The uploaded PDF changed or exceeds the allowed size. Upload it again.");
        using var staged = await StageMoveAsync(plan, ct);
        if (staged != null && Convert.ToHexString(staged.Request.Sha256) != plan.Context.SourceFileHash)
            throw Conflict("The source PDF changed after preview. Nothing was changed. Preview again.");
        using var connection = Connection(); await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        await EnsureSchemaAsync(connection, transaction, ct);
        await EnsureUnusedActionAsync(connection, transaction, plan, ct);
        var current = await PrepareAsync(connection, transaction, plan.Request, ct);
        current = current with { SourceFileHash = staged == null ? null : Convert.ToHexString(staged.Request.Sha256),
            ReplacementPdf = replacement?.Info };
        if (!Same(current, plan.Context)) throw Conflict("The employee or skill changed after preview. Nothing was changed. Preview again.");
        validation = SkillRecoveryRules.Validate(plan.Request, current);
        if (validation != null) throw Conflict(validation);
        if (!authorized()) throw Conflict("Developer access or the preview expired. Nothing was changed.");
        var before = await ReadScopesAsync(connection, transaction, ScopesFor(plan), ct);
        var archiveCopies = new List<SkillRecoveryArchiveCopy>();
        Guid? created = null;
        if (plan.Request.Action == "undo")
            created = await ApplyUndoAsync(connection, transaction, current, archiveCopies, ct);
        else
        {
            var source = current.Source!;
            if (SkillRecoveryRules.ShouldReplaceConflicts(plan.Request))
                foreach (var conflict in current.Conflicts)
                    await ArchiveAndTrackAsync(connection, transaction, conflict, archiveCopies, ct);
            switch (plan.Request.Action)
            {
                case "reinstate":
                    using (var command = new SqlCommand("UPDATE dbo.tblQualified SET DisQualifiedBy=NULL,DisQualifiedDate=NULL,TheReason=NULL WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version", connection, transaction))
                    {
                        KeyParameters(command, source);
                        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The selected skill changed.");
                    }
                    break;
                case "restore":
                    created = await InsertAsync(connection, transaction, source.EmployeeCode, source.Values, ct);
                    break;
                case "undo-edit":
                    await ArchiveAndTrackAsync(connection, transaction, source, archiveCopies, ct);
                    created = await InsertAsync(connection, transaction, source.EmployeeCode, current.Prior!.Values, ct);
                    break;
                case "undo-add":
                    await DeleteExactAsync(connection, transaction, source, ct);
                    break;
                case "move":
                    var target = current.TargetEmployee!.EmployeeCode;
                    var fileId = staged == null ? (Guid?)null : (await repository.InsertVersionAsync(connection, transaction,
                        staged.Request, staged.Content, ct)).FileId;
                    if (source.Table == SkillRecoveryTable.Current) await ArchiveAndTrackAsync(connection, transaction, source, archiveCopies, ct);
                    created = await InsertAsync(connection, transaction, target, source.Values with { CertificateFileId = fileId, Download = null }, ct);
                    break;
                case "move-tab":
                    created = await MoveTabAsync(connection, transaction, plan, source, archiveCopies, ct);
                    break;
                case "remove-pdf":
                    await UpdatePdfAsync(connection, transaction, source, null, null, ct);
                    break;
                case "replace-pdf":
                    var replacementFile = await repository.InsertVersionAsync(connection, transaction,
                        replacement!.Stage.Request, replacement.Stage.Content, ct);
                    await UpdatePdfAsync(connection, transaction, source, replacementFile.FileId, null, ct);
                    break;
                default: throw Conflict("Choose a valid recovery action.");
            }
        }
        var after = await ReadScopesAsync(connection, transaction, before, ct);
        await WriteAuditAsync(connection, transaction, plan, before, after, created, archiveCopies, ct);
        if (!authorized()) throw Conflict("Developer access or the preview expired. Nothing was changed.");
        ct.ThrowIfCancellationRequested();
        transaction.Commit();
        activity?.MarkCommitted();
        replacement?.Consume();
        return new(plan.ActionId, "Recovery saved.");
    }

    private async Task<StagedEmployeeFile?> StageMoveAsync(SkillRecoveryPlan plan, CancellationToken ct)
    {
        var source = plan.Context.Source;
        if (plan.Request.Action != "move" || source == null || (!source.Values.CertificateFileId.HasValue && string.IsNullOrWhiteSpace(source.Values.Download))) return null;
        EnsureAttachmentWrites(plan.Context.TargetEmployee!.EmployeeCode);
        return await files.StageAsync(plan.Actor, Owner(source), plan.Context.TargetEmployee!.EmployeeCode, ct);
    }

    private static async Task<Guid?> MoveTabAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryPlan plan, SkillRecoveryRow source, List<SkillRecoveryArchiveCopy> archiveCopies, CancellationToken ct)
    {
        if (plan.Request.TargetTab == "obsolete")
        {
            await ArchiveAndTrackAsync(connection, transaction, source, archiveCopies, ct);
            return null;
        }
        var values = SkillRecoveryRules.ResultValues(plan.Request, source.Values, plan.Actor, DateTime.Now);
        if (source.Table == SkillRecoveryTable.Archive)
            return await InsertAsync(connection, transaction, source.EmployeeCode, values, ct);
        using var command = new SqlCommand("UPDATE dbo.tblQualified SET DisQualifiedBy=@by,DisQualifiedDate=@date,TheReason=@reason WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version", connection, transaction);
        KeyParameters(command, source);
        command.Parameters.Add("@by", SqlDbType.NVarChar, 128).Value = (object?)values.DisQualifiedBy ?? DBNull.Value;
        command.Parameters.Add("@date", SqlDbType.DateTime2).Value = (object?)values.DisQualifiedDate ?? DBNull.Value;
        command.Parameters.Add("@reason", SqlDbType.NVarChar, SkillRecoveryRules.DisqualificationReasonLimit).Value = (object?)values.TheReason ?? DBNull.Value;
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The selected skill changed.");
        return null;
    }

    private static async Task DeleteExactAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryRow row, CancellationToken ct)
    {
        using var command = new SqlCommand("DELETE " + Table(row.Table) + " WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version", connection, transaction);
        KeyParameters(command, row);
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The selected skill changed.");
    }

    private static async Task ArchiveAndTrackAsync(SqlConnection connection, SqlTransaction transaction,
        SkillRecoveryRow row, List<SkillRecoveryArchiveCopy> archiveCopies, CancellationToken ct)
    {
        if (row.Table != SkillRecoveryTable.Current) throw Conflict("Only an operational version can be removed.");
        var archiveRowId = Guid.NewGuid();
        using var command = new SqlCommand("INSERT dbo.tblQualified_Obsoleted (EmpCode,OcrSkillRowId," + ValueColumns + ") SELECT EmpCode,@archive," + ValueColumns + " FROM dbo.tblQualified WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version; IF @@ROWCOUNT<>1 THROW 51000,'The selected skill changed.',1; DELETE dbo.tblQualified WHERE EmpCode=@employee AND OcrSkillRowId=@row AND FileStorageVersion=@version; IF @@ROWCOUNT<>1 THROW 51000,'The selected skill changed.',1;", connection, transaction);
        KeyParameters(command, row);
        command.Parameters.Add("@archive", SqlDbType.UniqueIdentifier).Value = archiveRowId;
        await command.ExecuteNonQueryAsync(ct);
        archiveCopies.Add(new(row.Key, archiveRowId));
    }

    private static async Task<Guid> InsertAsync(SqlConnection connection, SqlTransaction transaction,
        string employee, SkillRecoveryValues values, CancellationToken ct,
        SkillRecoveryTable table = SkillRecoveryTable.Current, Guid? restoredRowId = null)
    {
        var rowId = restoredRowId ?? Guid.NewGuid();
        using var command = new SqlCommand("INSERT " + Table(table) + " (EmpCode,OcrSkillRowId," + ValueColumns + ") VALUES (@employee,@row," +
            string.Join(",", Fields.Select((_, index) => "@v" + index)) + ")", connection, transaction);
        EmployeeParameter(command, employee); command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = rowId;
        for (var index = 0; index < Fields.Length; index++)
        {
            var property = typeof(SkillRecoveryValues).GetProperty(Fields[index])!;
            var type = property.PropertyType == typeof(DateTime?) ? SqlDbType.DateTime2 :
                property.PropertyType == typeof(Guid?) ? SqlDbType.UniqueIdentifier : SqlDbType.NVarChar;
            var parameter = command.Parameters.Add("@v" + index, type);
            if (type == SqlDbType.NVarChar) parameter.Size = -1;
            parameter.Value = property.GetValue(values) ?? DBNull.Value;
        }
        if (await command.ExecuteNonQueryAsync(ct) != 1) throw Conflict("The restored skill could not be saved.");
        return rowId;
    }

    private static void KeyParameters(SqlCommand command, SkillRecoveryRow row)
    {
        EmployeeParameter(command, row.EmployeeCode);
        command.Parameters.Add("@row", SqlDbType.UniqueIdentifier).Value = row.RowId;
        command.Parameters.Add("@version", SqlDbType.Binary, 8).Value = Convert.FromBase64String(row.Version);
    }
}
