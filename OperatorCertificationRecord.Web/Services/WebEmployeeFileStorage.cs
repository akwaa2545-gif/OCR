using OperatorCertificationRecord.FileStorage;
using System.Data;
using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Resolves owner references before opening SQL content; employment status never changes read routing.</summary>
public sealed class WebEmployeeFileStorage
{
    private readonly IEmployeeFileRepository _repository;
    private readonly FileStorageOptions _options;
    private readonly IConfiguration? _configuration;

    public WebEmployeeFileStorage(IEmployeeFileRepository repository, FileStorageOptions options, IConfiguration? configuration = null)
    {
        _repository = repository;
        _options = options;
        _configuration = configuration;
    }

    public async Task<StoredFileLease?> OpenLegacyPhotoAsync(string filename, CancellationToken ct)
    {
        var code = Path.GetFileNameWithoutExtension(filename).Split('_')[0];
        var codeIsValid = !string.IsNullOrWhiteSpace(code) && code.Length <= 10 && code.All(c => char.IsLetterOrDigit(c) || c == '-');
        if (codeIsValid)
        {
            var alias = await OpenAliasAsync(code, FileKind.Photo, filename, ct);
            if (alias != null) return alias;
        }
        if (_configuration != null)
        {
            if (!await _repository.IsSchemaAvailableAsync(ct))
                await LegacyFileAuthorityGuard.EnsureUnlinkedAsync(_configuration, null, FileKind.Photo, ct, filename: filename);
            else
            {
                var exact = await OpenExactPhotoReferenceAsync(filename, ct);
                if (exact != null) return exact;
            }
        }
        if (!codeIsValid) return null;
        FileOwnerState? state;
        try { state = await GetOwnerAsync(new FileOwner(FileOwnerTable.Employee, code), ct); }
        catch (FileNotFoundException) { return null; }
        if (state?.FileId == null) return null;
        // A migrated owner never returns to a guessed disk filename, including historical session URLs.
        _options.RequireSqlRead();
        if (!string.Equals(Path.GetFileName((state.LegacyPath ?? "").Replace('\\', '/')), filename, StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("The legacy photo requires an approved alias.");
        return await _repository.OpenReadAsync(state.FileId.Value, code, FileKind.Photo, ct);
    }

    private async Task<StoredFileLease?> OpenExactPhotoReferenceAsync(string filename, CancellationToken ct)
    {
        var connectionString = _configuration?.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString)) return null;
        using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);
        // Resolve the employee's exact legacy reference, never search blob metadata by an unscoped filename.
        using var command = new SqlCommand("SELECT EmpCode,PhotoFileId,Photo FROM dbo.tblEmployee WHERE PhotoFileId IS NOT NULL", connection);
        var matches = new List<(string Employee, Guid FileId)>();
        using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct))
                if (string.Equals(Path.GetFileName((reader[2] as string ?? "").Replace('\\', '/')), filename, StringComparison.OrdinalIgnoreCase))
                    matches.Add((reader.GetString(0), reader.GetGuid(1)));
        if (matches.Count == 0) return null;
        if (matches.Count != 1) throw new InvalidOperationException("The legacy photo reference is ambiguous.");
        var approvedAlias = await OpenAliasAsync(matches[0].Employee, FileKind.Photo, filename, ct);
        if (approvedAlias != null) return approvedAlias;
        _options.RequireSqlRead();
        return await _repository.OpenReadAsync(matches[0].FileId, matches[0].Employee, FileKind.Photo, ct);
    }

    public async Task<StoredFileLease?> OpenLegacyCertificateAsync(string employee, string filename, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(employee) || employee.Length > 10) throw new ArgumentException("Invalid employee.");
        var alias = await OpenAliasAsync(employee, FileKind.Certificate, employee + "/" + filename, ct)
            ?? await OpenAliasAsync(employee, FileKind.Certificate, filename, ct);
        if (alias != null) return alias;
        // Historical legacy URLs contain only a filename. Match the stored physical path for this owner,
        // across both version tables, rather than using an unscoped filename-to-blob lookup.
        if (_configuration == null) return null;
        if (!await _repository.IsSchemaAvailableAsync(ct))
        {
            await LegacyFileAuthorityGuard.EnsureUnlinkedAsync(_configuration, employee, FileKind.Certificate, ct, filename: filename);
            return null;
        }
        using var connection = new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        using var command = new SqlCommand("SELECT CertificateFileId,Download FROM dbo.tblQualified WHERE EmpCode=@employee UNION ALL SELECT CertificateFileId,Download FROM dbo.tblQualified_Obsoleted WHERE EmpCode=@employee", connection);
        command.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employee;
        var ids = new HashSet<Guid>();
        using (var reader = await command.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
                if (reader[0] is Guid id && string.Equals(Path.GetFileName((reader[1] as string ?? "").Replace('\\', '/')), filename, StringComparison.OrdinalIgnoreCase)) ids.Add(id);
        }
        if (ids.Count == 0) return null;
        if (ids.Count != 1) throw new InvalidOperationException("The legacy attachment is ambiguous.");
        _options.RequireSqlRead();
        return await _repository.OpenReadAsync(ids.Single(), employee, FileKind.Certificate, ct);
    }

    public async Task<FileOwnerState?> GetOwnerAsync(FileOwner owner, CancellationToken ct)
    {
        if (!await _repository.IsSchemaAvailableAsync(ct))
        {
            if (_configuration != null && owner.Table == FileOwnerTable.Employee)
                await LegacyFileAuthorityGuard.EnsureUnlinkedAsync(_configuration, owner.EmployeeCode, FileKind.Photo, ct);
            return null;
        }
        var state = await _repository.GetOwnerStateAsync(owner, ct);
        return state.FileId.HasValue ? state : null;
    }

    public async Task<StoredFileLease> OpenOwnerAsync(FileOwner owner, Guid fileId, CancellationToken ct)
    {
        var state = await GetOwnerAsync(owner, ct);
        if (state?.FileId != fileId) throw new FileNotFoundException("Attachment not found.");
        _options.RequireSqlRead();
        var kind = owner.Table == FileOwnerTable.Employee ? FileKind.Photo : FileKind.Certificate;
        return await _repository.OpenReadAsync(fileId, owner.EmployeeCode, kind, ct);
    }

    public async Task<StoredFileLease> OpenCurrentPhotoAsync(string employee, CancellationToken ct)
    {
        var owner = new FileOwner(FileOwnerTable.Employee, employee);
        var state = await GetOwnerAsync(owner, ct);
        if (state?.FileId == null) throw new FileNotFoundException("Photo not found.");
        _options.RequireSqlRead();
        // Read the immutable version observed with this owner state even if a replacement commits meanwhile.
        return await _repository.OpenReadAsync(state.FileId.Value, employee, FileKind.Photo, ct);
    }

    public async Task<StoredFileLease?> OpenAliasAsync(string employeeCode, FileKind kind, string relativeKey, CancellationToken ct)
    {
        if (!await _repository.IsSchemaAvailableAsync(ct)) return null;
        var id = await _repository.ResolveAliasAsync(employeeCode, kind, relativeKey, ct);
        if (!id.HasValue) return null;
        _options.RequireSqlRead();
        return await _repository.OpenReadAsync(id.Value, employeeCode, kind, ct);
    }
}
