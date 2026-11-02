using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed class FileStorageOptions
    {
        public FileStorageOptions(
            bool readSqlEnabled = false,
            bool writeSqlEnabled = false,
            IEnumerable<string>? pilotEmployeeCodes = null,
            long maxPhotoBytes = 5242880,
            long maxCertificateBytes = 67108864
        )
        {
            if (writeSqlEnabled && !readSqlEnabled)
                throw new ArgumentException("SQL writes require SQL reads.");
            if (maxPhotoBytes <= 0 || maxCertificateBytes <= 0)
                throw new ArgumentOutOfRangeException("File limits must be positive.");
            ReadSqlEnabled = readSqlEnabled;
            WriteSqlEnabled = writeSqlEnabled;
            MaxPhotoBytes = maxPhotoBytes;
            MaxCertificateBytes = maxCertificateBytes;
            PilotEmployeeCodes = new ReadOnlyCollection<string>(
                (pilotEmployeeCodes ?? Enumerable.Empty<string>())
                    .Select(FileValidation.EmployeeCode)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            );
        }

        public bool ReadSqlEnabled { get; }
        public bool WriteSqlEnabled { get; }
        // Retained for configuration compatibility; SQL writes no longer use an employee allowlist.
        public IReadOnlyList<string> PilotEmployeeCodes { get; }
        public long MaxPhotoBytes { get; }
        public long MaxCertificateBytes { get; }

        public bool ShouldWriteSql(string employeeCode, Guid? existingFileId)
        {
            FileValidation.EmployeeCode(employeeCode);
            // Publication transactions still enforce current employment and owner concurrency.
            if (existingFileId.HasValue && !WriteSqlEnabled)
                throw new InvalidOperationException(
                    "This attachment is SQL-authoritative; enable SQL writes to save changes."
                );
            return WriteSqlEnabled;
        }

        public void RequireSqlRead()
        {
            if (!ReadSqlEnabled)
                throw new InvalidOperationException(
                    "This attachment requires SQL reads. Enable SQL reads to access it."
                );
        }
    }
}
