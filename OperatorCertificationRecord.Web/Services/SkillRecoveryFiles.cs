using System.Data;
using System.Data.SqlClient;
using System.Text.Json;
using OperatorCertificationRecord.FileStorage;

namespace OperatorCertificationRecord.Web.Services;

public sealed class SkillRecoveryPdf(Stream content, string fileName, IDisposable? resources = null) : IDisposable
{
    public Stream Content { get; } = content;
    public string FileName { get; } = fileName;
    public void Dispose() { if (resources != null) resources.Dispose(); else Content.Dispose(); }
}

public interface ISkillRecoveryFileSource
{
    Task<FileOwnerState> GetOwnerAsync(FileOwner owner, CancellationToken ct);
    Task<FileOwnerState> GetHistoryOwnerAsync(Guid actionId, CancellationToken ct) =>
        Task.FromException<FileOwnerState>(new FileNotFoundException("The retained skill PDF is unavailable."));
    Task<SkillRecoveryPdf> OpenSqlAsync(FileOwnerState owner, CancellationToken ct);
    Task<SkillRecoveryPdf> OpenLegacyAsync(string employeeCode, string storedDownload, CancellationToken ct);
}

/// <summary>Developer-authorized exact-owner reads and target-owned staging, without changing skill rows.</summary>
public sealed class SkillRecoveryFiles(DeveloperRoleService developers, ISkillRecoveryFileSource source, FileStorageOptions options)
{
    public async Task<SkillRecoveryPdf> OpenAsync(string actor, FileOwner owner, CancellationToken ct)
    {
        RequireDeveloper(actor);
        ValidateEmployee(owner.EmployeeCode);
        if (owner.Table is not (FileOwnerTable.Qualified or FileOwnerTable.Obsolete) || !owner.SkillRowId.HasValue)
            throw new ArgumentException("Select an exact skill record.");
        var state = await source.GetOwnerAsync(owner, ct);
        if (state.Owner.Table != owner.Table || state.Owner.EmployeeCode != owner.EmployeeCode || state.Owner.SkillRowId != owner.SkillRowId)
            throw new InvalidDataException("The attachment owner could not be verified.");
        return await OpenStateAsync(actor, state, ct);
    }

    public async Task<SkillRecoveryPdf> OpenHistoryAsync(string actor, Guid actionId, CancellationToken ct)
    {
        RequireDeveloper(actor);
        if (actionId == Guid.Empty) throw new ArgumentException("Select a recovery action.");
        var state = await source.GetHistoryOwnerAsync(actionId, ct);
        ValidateEmployee(state.Owner.EmployeeCode);
        if (state.Owner.Table is not (FileOwnerTable.Qualified or FileOwnerTable.Obsolete) || !state.Owner.SkillRowId.HasValue)
            throw new InvalidDataException("The retained attachment owner could not be verified.");
        return await OpenStateAsync(actor, state, ct);
    }

    private async Task<SkillRecoveryPdf> OpenStateAsync(string actor, FileOwnerState state, CancellationToken ct)
    {
        SkillRecoveryPdf result;
        if (state.FileId.HasValue)
        {
            options.RequireSqlRead();
            result = await source.OpenSqlAsync(state, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(state.LegacyPath)) throw new FileNotFoundException("This skill has no linked PDF.");
            _ = SafeFileName(state.LegacyPath);
            result = await source.OpenLegacyAsync(state.Owner.EmployeeCode, state.LegacyPath, ct);
        }
        return AuthorizedResult(actor, result);
    }

    public async Task<StagedEmployeeFile> StageLegacyAsync(string actor, string sourceEmployee, string storedDownload,
        string destinationEmployee, CancellationToken ct)
    {
        RequireDeveloper(actor);
        ValidateEmployee(sourceEmployee);
        ValidateEmployee(destinationEmployee);
        var filename = SafeFileName(storedDownload);
        if (!options.ShouldWriteSql(destinationEmployee, null))
            throw new InvalidOperationException("SQL attachment writes are not enabled.");
        using var input = AuthorizedResult(actor, await source.OpenLegacyAsync(sourceEmployee, storedDownload, ct));
        var staged = await FileStager.StageAsync(input.Content, destinationEmployee, FileKind.Certificate,
            filename, actor, options, ct, "skill-recovery:" + sourceEmployee);
        try { RequireDeveloper(actor); return staged; }
        catch { staged.Dispose(); throw; }
    }

    public async Task<StagedEmployeeFile> StageAsync(string actor, FileOwner owner, string destinationEmployee, CancellationToken ct)
    {
        RequireDeveloper(actor);
        ValidateEmployee(destinationEmployee);
        if (!options.ShouldWriteSql(destinationEmployee, null))
            throw new InvalidOperationException("SQL attachment writes are not enabled.");
        using var input = await OpenAsync(actor, owner, ct);
        var staged = await FileStager.StageAsync(input.Content, destinationEmployee, FileKind.Certificate,
            input.FileName, actor, options, ct, "skill-recovery:" + owner.EmployeeCode + ":" + owner.SkillRowId);
        try { RequireDeveloper(actor); return staged; }
        catch { staged.Dispose(); throw; }
    }

    private SkillRecoveryPdf AuthorizedResult(string actor, SkillRecoveryPdf result)
    {
        try { RequireDeveloper(actor); return result; }
        catch { result.Dispose(); throw; }
    }

    private void RequireDeveloper(string actor)
    {
        if (!developers.IsDeveloper(actor)) throw new UnauthorizedAccessException("Developer access is required.");
    }

    internal static void ValidateEmployee(string employee)
    {
        if (string.IsNullOrWhiteSpace(employee) || employee.Length > 10 ||
            !employee.All(character => char.IsAsciiLetterOrDigit(character) || character == '-'))
            throw new ArgumentException("Select a valid employee.");
    }

    internal static string SafeFileName(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Length > 1024 || reference.Contains("://", StringComparison.Ordinal) ||
            reference.Any(char.IsControl)) throw new ArgumentException("The attachment reference needs review.");
        var parts = reference.Replace('\\', '/').Split('/');
        var filename = parts.Last();
        if (parts.Any(part => part is "." or "..") || filename.Length is < 1 or > 255 ||
            filename.EndsWith(' ') || filename.EndsWith('.') || filename.Any(character => "<>:\"|?*%".Contains(character)))
            throw new ArgumentException("The attachment reference needs review.");
        return filename;
    }
}

/// <summary>Storage routing matches certificate serving: SQL first, then only configured physical roots.</summary>
public sealed class SkillRecoveryFileSource(IEmployeeFileRepository repository, WebEmployeeFileStorage sqlFiles,
    IConfiguration configuration, FileStorageOptions options) : ISkillRecoveryFileSource
{
    public async Task<FileOwnerState> GetOwnerAsync(FileOwner owner, CancellationToken ct)
    {
        if (!await repository.IsSchemaAvailableAsync(ct))
            throw new InvalidOperationException("Skill attachment storage is not ready.");
        return await repository.GetOwnerStateAsync(owner, ct);
    }

    public async Task<FileOwnerState> GetHistoryOwnerAsync(Guid actionId, CancellationToken ct)
    {
        if (actionId == Guid.Empty) throw new ArgumentException("Select a recovery action.");
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) throw new InvalidOperationException("Recovery history storage is unavailable.");
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT Operation,SourceEmployee,SourceRowId,PlanJson FROM dbo.tblSkillRecoveryAction WHERE ActionId=@action AND Operation IN (N'undo-add',N'remove-pdf',N'replace-pdf')", connection);
        command.Parameters.Add("@action", SqlDbType.UniqueIdentifier).Value = actionId;
        using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, ct);
        if (!await reader.ReadAsync(ct)) throw new FileNotFoundException("No retained PDF was found for this action.");
        return ParseHistoryOwner(actionId, reader.GetString(0), reader.GetString(1).Trim(), reader.GetGuid(2), reader.GetString(3));
    }

    internal static FileOwnerState ParseHistoryOwner(Guid actionId, string operation, string employee, Guid rowId, string planJson)
    {
        if (operation is not ("undo-add" or "remove-pdf" or "replace-pdf")) throw new FileNotFoundException("No retained PDF was found for this action.");
        SkillRecoveryPlan? plan;
        try
        {
            if (planJson.Length > 4 * 1024 * 1024) throw new InvalidDataException("The retained skill snapshot is invalid.");
            plan = JsonSerializer.Deserialize<SkillRecoveryPlan>(planJson);
        }
        catch (JsonException error) { throw new InvalidDataException("The retained skill snapshot is invalid.", error); }
        var snapshot = plan?.Context?.Source;
        if (plan == null || actionId == Guid.Empty || rowId == Guid.Empty || plan.ActionId != actionId ||
            plan.Request == null || plan.Request.Action != operation || plan.Request.EmployeeCode != employee ||
            plan.Request.SourceTable is not (SkillRecoveryTable.Current or SkillRecoveryTable.Archive) || plan.Request.SourceRowId != rowId ||
            plan.Context?.SourceEmployee?.EmployeeCode != employee || snapshot == null ||
            snapshot.Table != plan.Request.SourceTable || snapshot.EmployeeCode != employee || snapshot.RowId != rowId || snapshot.Values == null)
            throw new InvalidDataException("The retained skill ownership could not be verified.");
        SkillRecoveryFiles.ValidateEmployee(employee);
        byte[] version;
        try { version = Convert.FromBase64String(snapshot.Version); }
        catch (Exception error) when (error is ArgumentException or FormatException)
        { throw new InvalidDataException("The retained skill version is invalid.", error); }
        if (version.Length != 8) throw new InvalidDataException("The retained skill version is invalid.");
        var ownerTable = snapshot.Table == SkillRecoveryTable.Archive ? FileOwnerTable.Obsolete : FileOwnerTable.Qualified;
        return new(new FileOwner(ownerTable, employee, rowId), snapshot.Values.CertificateFileId, version,
            snapshot.Values.Download, plan.Context.SourceEmployee.ResignDate, plan.Context.SourceEmployee.StatusWork);
    }

    public async Task<SkillRecoveryPdf> OpenSqlAsync(FileOwnerState owner, CancellationToken ct)
    {
        options.RequireSqlRead();
        if (owner.FileId is not Guid fileId) throw new FileNotFoundException("No SQL attachment is linked.");
        var lease = await repository.OpenReadAsync(fileId, owner.Owner.EmployeeCode, FileKind.Certificate, ct);
        return WrapSql(lease, owner.Owner.EmployeeCode, fileId);
    }

    public async Task<SkillRecoveryPdf> OpenLegacyAsync(string employeeCode, string storedDownload, CancellationToken ct)
    {
        SkillRecoveryFiles.ValidateEmployee(employeeCode);
        var filename = SkillRecoveryFiles.SafeFileName(storedDownload);
        // Exceptions or SQL-owned legacy references must never downgrade to a disk copy.
        var sql = await sqlFiles.OpenLegacyCertificateAsync(employeeCode, filename, ct);
        if (sql != null) return WrapSql(sql, employeeCode, sql.Metadata.FileId);
        Exception? inaccessible = null;
        foreach (var location in Locations(employeeCode, filename))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var result = OpenPhysical(location.Root, location.Relative, location.Required);
                if (result != null) return result;
            }
            catch (InvalidDataException) { throw; }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                inaccessible ??= error;
            }
        }
        if (inaccessible != null) throw new IOException("The linked PDF storage is unavailable.", inaccessible);
        throw new FileNotFoundException("The linked PDF could not be found.");
    }

    private static SkillRecoveryPdf WrapSql(StoredFileLease lease, string employee, Guid fileId)
    {
        try
        {
            if (lease.Metadata.EmployeeCode != employee || lease.Metadata.FileId != fileId ||
                lease.Metadata.Kind != FileKind.Certificate || lease.Metadata.ContentType != "application/pdf")
                throw new InvalidDataException("The PDF ownership or type could not be verified.");
            return new(lease.Content, SkillRecoveryFiles.SafeFileName(lease.Metadata.OriginalFileName), lease);
        }
        catch { lease.Dispose(); throw; }
    }

    private IEnumerable<(string Root, string Relative, bool Required)> Locations(string employee, string filename)
    {
        var web = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        var uploads = configuration["LocalUploadSubfolder"] ?? "uploads";
        var rooted = Path.IsPathRooted(uploads);
        yield return (rooted ? uploads : Path.Combine(web, uploads), Path.Combine(employee, filename), rooted);
        yield return (Path.Combine(web, "uploads"), Path.Combine(employee, filename), false);
        yield return (Path.Combine(web, "uploads"), filename, false);
        yield return (Path.Combine(web, "certs"), Path.Combine(employee, filename), false);
        yield return (Path.Combine(web, "certs"), filename, false);
    }

    private SkillRecoveryPdf? OpenPhysical(string root, string relative, bool required)
    {
        var rootPath = Path.GetFullPath(root);
        try
        {
            if ((File.GetAttributes(rootPath) & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != FileAttributes.Directory)
                throw new IOException("The attachment root needs review.");
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            if (required || rootPath.StartsWith("\\\\", StringComparison.Ordinal))
                throw new IOException("The attachment root is unavailable.", error);
            return null;
        }
        var path = Path.GetFullPath(Path.Combine(rootPath, relative));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(Path.TrimEndingDirectorySeparator(rootPath) + Path.DirectorySeparatorChar, comparison))
            throw new IOException("The attachment reference needs review.");
        try
        {
            var directory = Path.GetDirectoryName(path)!;
            if (!string.Equals(directory, rootPath, comparison) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The attachment directory needs review.");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The attachment file needs review.");
            var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            try
            {
                if (input.Length is < 5 || input.Length > options.MaxCertificateBytes)
                    throw new InvalidDataException("The PDF size is invalid.");
                Span<byte> prefix = stackalloc byte[5];
                input.ReadExactly(prefix);
                if (!prefix.SequenceEqual("%PDF-"u8)) throw new InvalidDataException("The linked file is not a PDF.");
                input.Position = 0;
                return new(input, Path.GetFileName(path));
            }
            catch { input.Dispose(); throw; }
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
}
