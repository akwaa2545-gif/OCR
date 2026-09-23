using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace OperatorCertificationRecord.Web.Services;

public sealed class EmployeePhotoStorageService
{
    private const long DefaultMaxFileBytes = 5 * 1024 * 1024;
    private static readonly Regex EmployeeCodePattern =
        new("^[A-Za-z0-9-]{1,32}$", RegexOptions.CultureInvariant);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> EmployeeLocks =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly IConfiguration _configuration;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EmployeePhotoStorageService> _logger;

    public EmployeePhotoStorageService(
        IConfiguration configuration,
        TimeProvider timeProvider,
        ILogger<EmployeePhotoStorageService> logger)
    {
        _configuration = configuration;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    internal Task<PhotoStorageResult> StoreAsync(
        string employeeCode,
        IFormFile photo,
        CancellationToken cancellationToken = default) =>
        StoreAndCommitAsync(
            employeeCode,
            photo,
            (_, _) => Task.FromResult(true),
            cancellationToken);

    public async Task<PhotoStorageResult> StoreAndCommitAsync(
        string employeeCode,
        IFormFile photo,
        Func<PhotoStorageResult, CancellationToken, Task<bool>> persistAsync,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmployeeCode = ValidateEmployeeCode(employeeCode);
        var extension = ValidateUploadMetadata(photo);
        ArgumentNullException.ThrowIfNull(persistAsync);
        var uploadRoot = GetRequiredSetting("PhotoStorage:UploadRoot");
        var legacyRoot = GetRequiredSetting("PhotoStorage:LegacyMirrorRoot");
        var databasePrefix = GetRequiredSetting("PhotoStorage:DatabasePathPrefix").TrimEnd('/');

        var employeeLock = EmployeeLocks.GetOrAdd(
            normalizedEmployeeCode,
            _ => new SemaphoreSlim(1, 1));
        await employeeLock.WaitAsync(cancellationToken);

        var fileTicks = _timeProvider.GetUtcNow().Ticks;
        var fileName = $"{normalizedEmployeeCode}_{fileTicks}{extension}";
        var sourcePath = Path.Combine(uploadRoot, fileName);
        while (File.Exists(sourcePath))
        {
            fileName = $"{normalizedEmployeeCode}_{++fileTicks}{extension}";
            sourcePath = Path.Combine(uploadRoot, fileName);
        }
        var sourceTemporaryPath = sourcePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var legacyPath = Path.Combine(legacyRoot, normalizedEmployeeCode + extension);
        string? legacyBackupPath = null;
        var mirrorPublished = false;

        try
        {
            Directory.CreateDirectory(uploadRoot);
            await using (var output = new FileStream(
                sourceTemporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await photo.CopyToAsync(output, cancellationToken);
            }

            ValidateImageSignature(sourceTemporaryPath, extension);
            File.Move(sourceTemporaryPath, sourcePath);

            try
            {
                legacyBackupPath = BackupLegacyMirror(legacyPath);
                WriteLegacyMirrorAtomically(sourcePath, legacyPath);
                mirrorPublished = true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                TryDelete(sourcePath);
                RestoreLegacyMirror(legacyPath, legacyBackupPath, mirrorPublished);
                _logger.LogError(
                    ex,
                    "Failed to create legacy photo mirror for employee {EmployeeCode}",
                    normalizedEmployeeCode);
                throw new PhotoCompatibilityCopyException(
                    "The photo could not be synchronized with the legacy application.",
                    ex);
            }

            var result = new PhotoStorageResult(
                $"{databasePrefix}/{fileName}",
                sourcePath,
                legacyPath);

            try
            {
                if (!await persistAsync(result, cancellationToken))
                {
                    throw new PhotoPersistenceException("The photo could not be saved.");
                }
            }
            catch (Exception ex)
            {
                TryDelete(sourcePath);
                RestoreLegacyMirror(legacyPath, legacyBackupPath, mirrorPublished);
                if (ex is OperationCanceledException)
                {
                    throw;
                }

                throw;
            }

            TryDelete(legacyBackupPath);
            return result;
        }
        catch
        {
            TryDelete(sourceTemporaryPath);
            throw;
        }
        finally
        {
            employeeLock.Release();
        }
    }

    private string ValidateUploadMetadata(IFormFile photo)
    {
        if (photo == null || photo.Length == 0)
        {
            throw new PhotoUploadValidationException("Please select a non-empty photo.");
        }

        var maxFileBytes = _configuration.GetValue<long?>("PhotoStorage:MaxFileBytes")
            ?? DefaultMaxFileBytes;
        if (photo.Length > maxFileBytes)
        {
            throw new PhotoUploadValidationException("The photo is larger than the allowed limit.");
        }

        var extension = Path.GetExtension(photo.FileName).ToLowerInvariant();
        var isJpeg = (extension == ".jpg" || extension == ".jpeg") &&
                     string.Equals(photo.ContentType, "image/jpeg", StringComparison.OrdinalIgnoreCase);
        var isPng = extension == ".png" &&
                    string.Equals(photo.ContentType, "image/png", StringComparison.OrdinalIgnoreCase);

        if (!isJpeg && !isPng)
        {
            throw new PhotoUploadValidationException("Only JPG, JPEG, and PNG photos are allowed.");
        }

        return extension;
    }

    private static string ValidateEmployeeCode(string employeeCode)
    {
        var normalized = employeeCode?.Trim() ?? string.Empty;
        if (!EmployeeCodePattern.IsMatch(normalized))
        {
            throw new PhotoUploadValidationException("The employee code is invalid.");
        }

        return normalized;
    }

    private static void ValidateImageSignature(string path, string extension)
    {
        Span<byte> header = stackalloc byte[8];
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytesRead = stream.Read(header);
        var isJpeg = bytesRead >= 3 &&
                     header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = bytesRead >= 8 &&
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                    header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

        if ((extension == ".png" && !isPng) ||
            ((extension == ".jpg" || extension == ".jpeg") && !isJpeg))
        {
            throw new PhotoUploadValidationException("The uploaded file is not a valid image.");
        }
    }

    private static void WriteLegacyMirrorAtomically(string sourcePath, string legacyPath)
    {
        var legacyDirectory = Path.GetDirectoryName(legacyPath)
            ?? throw new InvalidOperationException("The legacy photo path is invalid.");
        Directory.CreateDirectory(legacyDirectory);

        var temporaryPath = Path.Combine(
            legacyDirectory,
            "." + Path.GetFileName(legacyPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

        try
        {
            File.Copy(sourcePath, temporaryPath, overwrite: false);
            File.Move(temporaryPath, legacyPath, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static string? BackupLegacyMirror(string legacyPath)
    {
        var legacyDirectory = Path.GetDirectoryName(legacyPath)
            ?? throw new InvalidOperationException("The legacy photo path is invalid.");
        Directory.CreateDirectory(legacyDirectory);
        if (!File.Exists(legacyPath))
        {
            return null;
        }

        var backupPath = Path.Combine(
            legacyDirectory,
            "." + Path.GetFileName(legacyPath) + "." + Guid.NewGuid().ToString("N") + ".bak");
        File.Copy(legacyPath, backupPath, overwrite: false);
        return backupPath;
    }

    private void RestoreLegacyMirror(string legacyPath, string? backupPath, bool mirrorPublished)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(backupPath) && File.Exists(backupPath))
            {
                File.Move(backupPath, legacyPath, overwrite: true);
            }
            else if (mirrorPublished)
            {
                TryDelete(legacyPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to restore legacy photo {LegacyPath}", legacyPath);
        }
    }

    private string GetRequiredSetting(string key)
    {
        var value = _configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Required setting '{key}' is missing.");
        }

        return value;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup; the original failure remains authoritative.
        }
    }
}

public sealed record PhotoStorageResult(string DatabasePath, string SourcePath, string LegacyPath);

public sealed class PhotoUploadValidationException : Exception
{
    public PhotoUploadValidationException(string message) : base(message) { }
}

public sealed class PhotoCompatibilityCopyException : Exception
{
    public PhotoCompatibilityCopyException(string message, Exception innerException)
        : base(message, innerException) { }
}

public sealed class PhotoPersistenceException : Exception
{
    public PhotoPersistenceException(string message) : base(message) { }

    public PhotoPersistenceException(string message, Exception innerException)
        : base(message, innerException) { }
}
