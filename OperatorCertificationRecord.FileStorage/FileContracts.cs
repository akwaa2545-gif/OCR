using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public enum FileKind
    {
        Photo,
        Certificate,
    }

    public enum FileOwnerTable
    {
        Employee,
        Qualified,
        Obsolete,
    }

    public sealed class FileOwner
    {
        public FileOwner(FileOwnerTable table, string employeeCode, Guid? skillRowId = null)
        {
            EmployeeCode = FileValidation.EmployeeCode(employeeCode);
            if (!Enum.IsDefined(typeof(FileOwnerTable), table))
                throw new ArgumentException("Unknown owner table.");
            if (
                table != FileOwnerTable.Employee
                && (!skillRowId.HasValue || skillRowId == Guid.Empty)
            )
                throw new ArgumentException("A stable skill row ID is required.");
            Table = table;
            SkillRowId = skillRowId;
        }

        public FileOwnerTable Table { get; }
        public string EmployeeCode { get; }
        public Guid? SkillRowId { get; }
    }

    public sealed class FileWriteRequest
    {
        private readonly byte[] hash;

        public FileWriteRequest(
            string employeeCode,
            FileKind kind,
            string originalFileName,
            string contentType,
            long byteLength,
            byte[] sha256,
            string createdBy,
            string? sourceKey = null
        )
        {
            EmployeeCode = FileValidation.EmployeeCode(employeeCode);
            FileValidation.Kind(kind);
            if (
                string.IsNullOrWhiteSpace(originalFileName)
                || originalFileName.Length > 255
                || Path.GetFileName(originalFileName) != originalFileName
            )
                throw new ArgumentException("A filename without directories is required.");
            if (string.IsNullOrWhiteSpace(contentType) || contentType.Length > 100)
                throw new ArgumentException("Invalid content type.");
            if (byteLength <= 0)
                throw new ArgumentOutOfRangeException(nameof(byteLength));
            if (sha256 == null || sha256.Length != 32)
                throw new ArgumentException("A SHA-256 hash is required.");
            if (string.IsNullOrWhiteSpace(createdBy) || createdBy.Length > 128)
                throw new ArgumentException("Invalid creating user.");
            if (sourceKey?.Length > 512)
                throw new ArgumentException("Source key exceeds 512 characters.");
            Kind = kind;
            OriginalFileName = originalFileName;
            ContentType = contentType;
            ByteLength = byteLength;
            hash = (byte[])sha256.Clone();
            CreatedBy = createdBy;
            SourceKey = sourceKey;
        }

        public string EmployeeCode { get; }
        public FileKind Kind { get; }
        public string OriginalFileName { get; }
        public string ContentType { get; }
        public long ByteLength { get; }
        public byte[] Sha256 => (byte[])hash.Clone();
        public string CreatedBy { get; }
        public string? SourceKey { get; }
    }

    public sealed class StoredFileMetadata
    {
        public StoredFileMetadata(Guid fileId, FileWriteRequest request, DateTime createdUtc)
        {
            FileId = fileId;
            Request = request ?? throw new ArgumentNullException(nameof(request));
            CreatedUtc = createdUtc;
        }

        public Guid FileId { get; }
        public FileWriteRequest Request { get; }
        public DateTime CreatedUtc { get; }
        public string EmployeeCode => Request.EmployeeCode;
        public FileKind Kind => Request.Kind;
        public string OriginalFileName => Request.OriginalFileName;
        public string ContentType => Request.ContentType;
        public long ByteLength => Request.ByteLength;
        public byte[] Sha256 => Request.Sha256;
    }

    public sealed class FileOwnerState
    {
        private readonly byte[] version;

        public FileOwnerState(
            FileOwner owner,
            Guid? fileId,
            byte[] rowVersion,
            string? legacyPath,
            DateTime? resignDate,
            string? statusWork
        )
        {
            Owner = owner;
            FileId = fileId;
            version = (byte[])rowVersion.Clone();
            LegacyPath = legacyPath;
            ResignDate = resignDate;
            StatusWork = statusWork;
        }

        public FileOwner Owner { get; }
        public Guid? FileId { get; }
        public byte[] RowVersion => (byte[])version.Clone();
        public string? LegacyPath { get; }
        public DateTime? ResignDate { get; }
        public string? StatusWork { get; }
        public bool IsCurrent => CurrentEmployeePolicy.IsCurrent(ResignDate, StatusWork);
    }

    public interface IEmployeeFileRepository
    {
        Task<bool> IsSchemaAvailableAsync(CancellationToken ct);
        Task<FileOwnerState> GetOwnerStateAsync(FileOwner owner, CancellationToken ct);
        Task<StoredFileLease> OpenReadAsync(
            Guid fileId,
            string employeeCode,
            FileKind kind,
            CancellationToken ct
        );
        Task<StoredFileMetadata> InsertVersionAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            FileWriteRequest request,
            Stream content,
            CancellationToken ct
        );
        Task LinkOwnerAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            FileOwner owner,
            Guid fileId,
            byte[] expectedVersion,
            CancellationToken ct
        );
        Task<Guid?> ResolveAliasAsync(
            string employeeCode,
            FileKind kind,
            string canonicalKey,
            CancellationToken ct
        );
        Task RegisterAliasAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string employeeCode,
            FileKind kind,
            string canonicalKey,
            Guid fileId,
            CancellationToken ct
        );
    }

    internal static class FileValidation
    {
        internal static string EmployeeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || code.Length > 10 || code != code.Trim())
                throw new ArgumentException(
                    "Employee code must contain 1 to 10 characters without surrounding whitespace.",
                    nameof(code)
                );
            return code;
        }

        internal static void Kind(FileKind kind)
        {
            if (!Enum.IsDefined(typeof(FileKind), kind))
                throw new ArgumentException("Unknown file kind.");
        }

        internal static SqlParameter EmployeeParameter(string code) =>
            new SqlParameter("@employee", SqlDbType.NVarChar, 10) { Value = EmployeeCode(code) };
    }
}
