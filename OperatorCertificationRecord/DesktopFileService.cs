using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Drawing;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using OperatorCertificationRecord.FileStorage;
using OperatorCertificationRecord.PhotoCompatibility;

namespace OperatorTrainingRecord
{
    /// <summary>SQL attachment boundary for the legacy client. A SQL reference never falls back to a share.</summary>
    public sealed class DesktopFileService
    {
        private readonly IEmployeeFileRepository repository;
        private readonly Func<SqlConnection> connectionFactory;
        private readonly FileStorageOptions options;
        private readonly Action<string, FileKind, Guid?> legacyFallbackAssert;
        private static readonly CancellationToken Token = CancellationToken.None;

        public DesktopFileService(string connectionString)
            : this(new SqlEmployeeFileRepository(() => new SqlConnection(connectionString)),
                  () => new SqlConnection(connectionString), ReadOptions()) { }

        public DesktopFileService(IEmployeeFileRepository repository, Func<SqlConnection> connectionFactory, FileStorageOptions options)
            : this(repository, connectionFactory, options, null) { }

        public DesktopFileService(IEmployeeFileRepository repository, Func<SqlConnection> connectionFactory, FileStorageOptions options,
            Action<string, FileKind, Guid?> legacyFallbackAssert)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.legacyFallbackAssert = legacyFallbackAssert;
        }

        private static FileStorageOptions ReadOptions()
        {
            bool read, write;
            bool.TryParse(ConfigurationManager.AppSettings["FileStorage:ReadSqlEnabled"], out read);
            bool.TryParse(ConfigurationManager.AppSettings["FileStorage:WriteSqlEnabled"], out write);
            var pilot = (ConfigurationManager.AppSettings["FileStorage:PilotEmployeeCodes"] ?? "")
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            return new FileStorageOptions(read, write, pilot);
        }

        public bool SchemaAvailable => repository.IsSchemaAvailableAsync(Token).GetAwaiter().GetResult();

        public string SkillProjection(string sql, string employeeCode = null)
        {
            if (!SchemaAvailable)
            {
                if (!string.IsNullOrWhiteSpace(employeeCode)) AssertLegacyFallback(employeeCode, FileKind.Certificate, null);
                return sql;
            }
            var marker = sql.IndexOf(" From tblQualified", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return sql;
            if (sql.Substring(0, marker).IndexOf('*') >= 0) return sql;
            var alias = sql.IndexOf("tblQualified q ", StringComparison.OrdinalIgnoreCase) >= 0 ? "q." : "";
            return sql.Insert(marker, "," + alias + "OcrSkillRowId," + alias + "CertificateFileId," + alias + "FileStorageVersion");
        }

        public static void ExecuteMutation(SqlCommand template, SqlConnection connection, SqlTransaction transaction, string sql)
        {
            using (var command = new SqlCommand(sql, connection, transaction))
            {
                foreach (SqlParameter parameter in template.Parameters)
                    command.Parameters.Add(((ICloneable)parameter).Clone());
                command.ExecuteNonQuery();
            }
        }

        public FileOwnerState GetOwner(FileOwner owner)
        {
            if (SchemaAvailable) return repository.GetOwnerStateAsync(owner, Token).GetAwaiter().GetResult();
            AssertLegacyFallback(owner.EmployeeCode, owner.Table == FileOwnerTable.Employee ? FileKind.Photo : FileKind.Certificate, owner.SkillRowId);
            return null;
        }

        private void AssertLegacyFallback(string employee, FileKind kind, Guid? rowId)
        {
            if (legacyFallbackAssert != null) { legacyFallbackAssert(employee, kind, rowId); return; }
            var sqlRepository = repository as SqlEmployeeFileRepository;
            if (sqlRepository != null)
                sqlRepository.AssertLegacyFallbackAllowedAsync(employee, kind, rowId, Token).GetAwaiter().GetResult();
        }

        public void AssertLegacyCertificateFallbackAllowed(string employeeCode, Guid? skillRowId = null)
        {
            AssertLegacyFallback(employeeCode, FileKind.Certificate, skillRowId);
        }

        public void ExecuteLegacyMutation(string employeeCode, FileKind kind, Guid? skillRowId,
            Action<SqlConnection, SqlTransaction> businessMutation)
        {
            // SQL publishers lock this same employee before inserting or linking any file version.
            // Hold the lock across the fallback decision, filesystem copy and business commit.
            new FileOwner(FileOwnerTable.Employee, employeeCode);
            if (businessMutation == null) throw new ArgumentNullException(nameof(businessMutation));
            using (var connection = connectionFactory())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    using (var gate = new SqlCommand("SELECT EmpCode FROM dbo.tblEmployee WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee", connection, transaction))
                    {
                        gate.Parameters.Add("@employee", SqlDbType.NVarChar, 10).Value = employeeCode;
                        gate.ExecuteScalar();
                    }
                    AssertLegacyFallback(employeeCode, kind, skillRowId);
                    businessMutation(connection, transaction);
                    transaction.Commit();
                }
            }
        }

        private bool ShouldWrite(string employeeCode, Guid? fileId)
        {
            var enabled = options.ShouldWriteSql(employeeCode, fileId);
            if (enabled && !SchemaAvailable)
                throw new InvalidOperationException("SQL file storage has not been deployed. Save cancelled.");
            return enabled;
        }

        public bool TrySavePhoto(string employeeCode, string sourcePath,
            Action<SqlConnection, SqlTransaction> businessMutation = null, bool createEmployee = false)
        {
            var owner = new FileOwner(FileOwnerTable.Employee, employeeCode);
            var before = createEmployee ? null : GetOwner(owner);
            if (!ShouldWrite(employeeCode, before == null ? null : before.FileId)) return false;
            var sqlRepository = RequireSqlRepository();
            var service = new EmployeeFileService(sqlRepository, connectionFactory, options);
            using (var source = File.OpenRead(sourcePath))
            using (var staged = service.StageAsync(source, employeeCode, FileKind.Photo,
                Path.GetFileName(sourcePath), Environment.UserName, Token).GetAwaiter().GetResult())
            using (var connection = connectionFactory())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    if (createEmployee && businessMutation != null) businessMutation(connection, transaction);
                    var current = sqlRepository.GetOwnerStateAsync(connection, transaction, owner, Token).GetAwaiter().GetResult();
                    if (before != null && !Equal(before.RowVersion, current.RowVersion))
                        throw new DBConcurrencyException("Employee changed before saving the photo. Reload and try again.");
                    Func<SqlConnection, SqlTransaction, CancellationToken, Task> mutate = null;
                    if (!createEmployee && businessMutation != null)
                        mutate = (c, t, ct) => { businessMutation(c, t); return Task.CompletedTask; };
                    service.PublishInTransactionAsync(connection, transaction, staged, owner, current.RowVersion, mutate, Token).GetAwaiter().GetResult();
                    transaction.Commit();
                    return true;
                }
            }
        }

        public bool TrySaveCertificate(string employeeCode, Guid? originalRowId, string sourcePath,
            Action<SqlConnection, SqlTransaction, Guid> businessMutation)
        {
            var original = originalRowId.HasValue ? GetOwner(new FileOwner(FileOwnerTable.Qualified, employeeCode, originalRowId)) : null;
            if (!ShouldWrite(employeeCode, original == null ? null : original.FileId)) return false;
            var sqlRepository = RequireSqlRepository();
            var service = new EmployeeFileService(sqlRepository, connectionFactory, options);
            using (var source = File.OpenRead(sourcePath))
            using (var staged = service.StageAsync(source, employeeCode, FileKind.Certificate,
                Path.GetFileName(sourcePath), Environment.UserName, Token).GetAwaiter().GetResult())
            using (var connection = connectionFactory())
            {
                connection.Open();
                using (var transaction = connection.BeginTransaction())
                {
                    CurrentEmployeePolicy.EnsureCurrentAsync(connection, transaction, employeeCode, Token).GetAwaiter().GetResult();
                    if (original != null)
                    {
                        var locked = sqlRepository.GetOwnerStateAsync(connection, transaction, original.Owner, Token).GetAwaiter().GetResult();
                        if (!Equal(original.RowVersion, locked.RowVersion)) throw new DBConcurrencyException("Skill changed. Reload and try again.");
                    }
                    var newRowId = Guid.NewGuid();
                    businessMutation(connection, transaction, newRowId);
                    var owner = new FileOwner(FileOwnerTable.Qualified, employeeCode, newRowId);
                    var state = sqlRepository.GetOwnerStateAsync(connection, transaction, owner, Token).GetAwaiter().GetResult();
                    service.PublishInTransactionAsync(connection, transaction, staged, owner, state.RowVersion, null, Token).GetAwaiter().GetResult();
                    transaction.Commit();
                    return true;
                }
            }
        }

        public void LoadPhoto(PictureBox target, string employeeCode, string legacyPath)
        {
            target.ImageLocation = null;
            var previous = target.Image;
            target.Image = null;
            if (previous != null) previous.Dispose();
            var state = GetOwner(new FileOwner(FileOwnerTable.Employee, employeeCode));
            Image replacement = null;
            if (state != null && state.FileId.HasValue)
            {
                options.RequireSqlRead();
                using (var lease = repository.OpenReadAsync(state.FileId.Value, employeeCode, FileKind.Photo, Token).GetAwaiter().GetResult())
                using (var bytes = new MemoryStream())
                {
                    if (lease.Metadata.ByteLength > options.MaxPhotoBytes)
                        throw new InvalidDataException("Stored photo exceeds its configured size limit.");
                    lease.Content.CopyTo(bytes, 81920); // Exhaust and verify before decoding.
                    bytes.Position = 0;
                    using (var decoded = Image.FromStream(bytes)) replacement = new Bitmap(decoded);
                }
            }
            else
            {
                var path = LegacyPhotoPathResolver.Resolve(legacyPath, employeeCode, @"\\svr120a\PhotoEmp$");
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    using (var decoded = Image.FromFile(path)) replacement = new Bitmap(decoded);
            }
            target.Image = replacement;
        }

        public void CopyCertificate(FileOwner owner, string legacyPath, string destination, Guid? expectedFileId = null)
        {
            var state = GetOwner(owner);
            if (expectedFileId.HasValue && (state == null || state.FileId != expectedFileId))
                throw new DBConcurrencyException("The selected certificate changed. Reload and try again.");
            if (state == null || !state.FileId.HasValue) { File.Copy(legacyPath, destination, true); return; }
            options.RequireSqlRead();
            var directory = Path.Combine(Path.GetTempPath(), "OperatorCertificationRecord", Guid.NewGuid().ToString("N"));
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User,
                FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(directory, security);
            var temporary = Path.Combine(directory, "certificate.pdf");
            try
            {
                using (var lease = repository.OpenReadAsync(state.FileId.Value, owner.EmployeeCode, FileKind.Certificate, Token).GetAwaiter().GetResult())
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920))
                    lease.Content.CopyTo(output, 81920);
                File.Copy(temporary, destination, true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); Directory.Delete(directory); }
        }

        private SqlEmployeeFileRepository RequireSqlRepository()
        {
            var result = repository as SqlEmployeeFileRepository;
            if (result == null) throw new InvalidOperationException("A SQL repository is required for publication.");
            return result;
        }

        private static bool Equal(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (var i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
