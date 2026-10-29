using System.Data;
using System.Data.SqlClient;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>One publication boundary for all three web certificate upload forms.</summary>
public sealed class EmployeeCertificateStorageService
{
    private readonly IConfiguration _configuration;
    private readonly EmployeeService _employees;
    private readonly SqlEmployeeFileRepository _repository;
    private readonly EmployeeFileService _files;
    private readonly FileStorageOptions _options;

    public EmployeeCertificateStorageService(IConfiguration configuration, EmployeeService employees,
        SqlEmployeeFileRepository repository, EmployeeFileService files, FileStorageOptions options)
    {
        _configuration = configuration;
        _employees = employees;
        _repository = repository;
        _files = files;
        _options = options;
    }

    public async Task<bool> SaveAsync(EmployeeQualifiedInput input, IFormFile? upload, string createdBy, CancellationToken ct)
    {
        if (upload == null || upload.Length == 0) return await _employees.AddOrUpdateQualifiedAsync(input);
        var employee = input.EmpCode ?? "";
        var schema = await _repository.IsSchemaAvailableAsync(ct);
        if (!schema)
            await LegacyFileAuthorityGuard.EnsureUnlinkedAsync(_configuration, employee, FileKind.Certificate, ct, process: input.ProcessName);
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        var before = schema ? await GetTargetAsync(connection, null, input, ct) : null;
        if (!_options.ShouldWriteSql(employee, before?.FileId)) return await SaveLegacyAsync(input, upload, ct);
        if (!schema) throw new InvalidOperationException("SQL file storage is not ready.");
        await using var source = upload.OpenReadStream();
        using var staged = await _files.StageAsync(source, employee, FileKind.Certificate, Path.GetFileName(upload.FileName), createdBy, ct);
        using var transaction = connection.BeginTransaction();
        await CurrentEmployeePolicy.EnsureCurrentAsync(connection, transaction, employee, ct);
        var locked = await GetTargetAsync(connection, transaction, input, ct);
        var expectedVersion = input.FileStorageVersion ?? before?.RowVersion;
        var expectedId = input.OcrSkillRowId ?? before?.Owner.SkillRowId;
        if (locked?.Owner.SkillRowId != expectedId || (locked != null && (expectedVersion == null || !locked.RowVersion.SequenceEqual(expectedVersion))))
            throw new DBConcurrencyException("The skill changed. Reload the employee before saving.");
        // Preserve the physical legacy reference. SQL identity, never a synthetic path, establishes authority.
        input.DownloadPath = locked?.LegacyPath;
        if (!await _employees.AddOrUpdateQualifiedInTransactionAsync(input, connection, transaction, ct))
            throw new DBConcurrencyException("The skill could not be saved.");
        var after = await GetTargetAsync(connection, transaction, input, ct)
            ?? throw new DBConcurrencyException("The skill could not be found after saving.");
        staged.Content.Position = 0;
        var file = await _repository.InsertVersionAsync(connection, transaction, staged.Request, staged.Content, ct);
        await CurrentEmployeePolicy.EnsureCurrentAsync(connection, transaction, employee, ct);
        await _repository.LinkOwnerAsync(connection, transaction, after.Owner, file.FileId, after.RowVersion, ct);
        transaction.Commit();
        return true;
    }

    private async Task<bool> SaveLegacyAsync(EmployeeQualifiedInput input, IFormFile upload, CancellationToken ct)
    {
        // The same bounded signature validator applies to legacy uploads; validation precedes filesystem publication.
        await using var source = upload.OpenReadStream();
        using var staged = await _files.StageAsync(source, input.EmpCode ?? "", FileKind.Certificate, Path.GetFileName(upload.FileName), "legacy-upload", ct);
        var fileName = CertificateStoragePaths.NewFileName(upload.FileName);
        var directory = CertificateStoragePaths.UploadDirectory(_configuration, input.EmpCode ?? "");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                staged.Content.Position = 0;
                await staged.Content.CopyToAsync(output, ct);
            }
            input.DownloadPath = CertificateStoragePaths.DownloadPath(_configuration, input.EmpCode, fileName);
            input.IsLegacyFileUpload = true;
            if (!await _employees.AddOrUpdateQualifiedAsync(input)) { File.Delete(path); return false; }
            return true;
        }
        catch { if (File.Exists(path)) File.Delete(path); throw; }
    }

    private static async Task<FileOwnerState?> GetTargetAsync(SqlConnection connection, SqlTransaction? transaction, EmployeeQualifiedInput input, CancellationToken ct)
    {
        const string sql = "SELECT q.OcrSkillRowId,q.CertificateFileId,q.FileStorageVersion,q.Download,e.ResignDate,e.StatusWork FROM dbo.tblQualified q WITH (UPDLOCK,HOLDLOCK) JOIN dbo.tblEmployee e ON e.EmpCode=q.EmpCode WHERE q.EmpCode=@employee AND q.ProcessName=@process AND (q.DisQualifiedBy IS NULL OR LTRIM(RTRIM(q.DisQualifiedBy))='')";
        using var command = new SqlCommand(sql, connection, transaction);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = input.EmpCode;
        command.Parameters.AddWithValue("@process", input.ProcessName ?? "");
        using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var state = new FileOwnerState(new FileOwner(FileOwnerTable.Qualified, input.EmpCode ?? "", reader.GetGuid(0)),
            reader.IsDBNull(1) ? null : reader.GetGuid(1), (byte[])reader[2], reader[3] as string, reader[4] as DateTime?, reader[5] as string);
        if (await reader.ReadAsync(ct)) throw new DBConcurrencyException("Multiple matching skills require reconciliation before saving.");
        return state;
    }
}
