using System.Data;
using System.Data.SqlClient;
using System.Text;

namespace OperatorCertificationRecord.Web.Services;

internal sealed record RoleActivitySqlMigrationResult(bool Imported, int DeveloperCount, int AdminCount);

/// <summary>Explicit import only. Normal requests never create or seed access-role storage.</summary>
internal static class RoleActivitySqlMigration
{
    internal static RoleActivitySqlMigrationResult Apply(IConfiguration configuration,
        string developerPath, string adminPath)
    {
        developerPath = Path.GetFullPath(developerPath);
        adminPath = Path.GetFullPath(adminPath);
        if (string.Equals(developerPath, adminPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Admin and Developer imports must use separate source files.");

        // Retain the same lock order as the legacy web writers while taking their final snapshot.
        using var developerGate = RoleActivityFileGate.Enter(developerPath);
        using var adminGate = RoleActivityFileGate.Enter(adminPath);
        var developers = RoleActivityFileStore.Read(developerPath) with { SchemaVersion = 1 };
        var admins = RoleActivityFileStore.Read(adminPath) with { SchemaVersion = 1 };
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new IOException("The database connection for access-role migration is not configured.");
        try
        {
            using var connection = new SqlConnection(connectionString);
            connection.Open();
            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            AcquireGate(connection, transaction);
            EnsureSchema(connection, transaction);
            var existing = ReadStores(connection, transaction);
            RoleActivitySqlMigrationResult result;
            if (existing.Count == 0)
            {
                Insert(connection, transaction, "Developer", developers);
                Insert(connection, transaction, "Admin", admins);
                result = new(true, developers.Members.Length, admins.Members.Length);
            }
            else
            {
                if (existing.Count != 2 || !existing.ContainsKey("Developer") || !existing.ContainsKey("Admin"))
                    throw new IOException("The SQL access-role store is only partially initialized or contains an unknown role.");
                foreach (var store in existing) VerifyMembers(connection, transaction, store.Key, store.Value);
                // Never replace newer SQL memberships, audit events or replay receipts from old source files.
                result = new(false, existing["Developer"].Members.Length, existing["Admin"].Members.Length);
            }
            transaction.Commit();
            return result;
        }
        catch (SqlException error)
        {
            throw new IOException("Access-role SQL migration did not complete. No partial import was committed.", error);
        }
    }

    private static void AcquireGate(SqlConnection connection, SqlTransaction transaction)
    {
        const string sql = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'OCR:AccessRoles'," +
            "@LockMode=N'Exclusive',@LockOwner=N'Transaction',@LockTimeout=5000; SELECT @result;";
        using var command = new SqlCommand(sql, connection, transaction);
        if (Convert.ToInt32(command.ExecuteScalar()) < 0)
            throw new IOException("Access-role storage is busy. No import was committed; retry after current role changes finish.");
    }

    private static void EnsureSchema(SqlConnection connection, SqlTransaction transaction)
    {
        using (var command = new SqlCommand(SchemaSql, connection, transaction)) command.ExecuteNonQuery();
        using var validate = new SqlCommand(ValidateSchemaSql, connection, transaction);
        if (Convert.ToInt32(validate.ExecuteScalar()) != 1)
            throw new IOException("The existing SQL access-role schema is unsupported. No import was committed.");
    }

    private static Dictionary<string, RoleActivityEnvelope> ReadStores(SqlConnection connection, SqlTransaction transaction)
    {
        using var command = new SqlCommand("SELECT RoleName,Envelope FROM dbo.tblApplicationRoleStore WITH(UPDLOCK,HOLDLOCK)", connection, transaction);
        using var reader = command.ExecuteReader();
        var stores = new Dictionary<string, RoleActivityEnvelope>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var envelope = RoleActivityFileStore.Parse(reader.GetString(1));
            if (envelope.SchemaVersion != 1) throw new IOException("The SQL access-role envelope version is unsupported.");
            stores.Add(reader.GetString(0), envelope);
        }
        return stores;
    }

    private static void VerifyMembers(SqlConnection connection, SqlTransaction transaction, string role, RoleActivityEnvelope envelope)
    {
        using var command = new SqlCommand("SELECT EmpCode FROM dbo.tblApplicationRoleMember WITH(UPDLOCK,HOLDLOCK) WHERE RoleName=@role", connection, transaction);
        command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
        using var reader = command.ExecuteReader();
        var members = new HashSet<string>(StringComparer.Ordinal);
        while (reader.Read()) members.Add(reader.GetString(0));
        if (!members.SetEquals(envelope.Members))
            throw new IOException("SQL role membership does not match its audited store. No import was committed.");
    }

    private static void Insert(SqlConnection connection, SqlTransaction transaction, string role, RoleActivityEnvelope envelope)
    {
        using (var command = new SqlCommand("INSERT dbo.tblApplicationRoleStore(RoleName,Envelope) VALUES(@role,@json)", connection, transaction))
        {
            command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
            command.Parameters.Add("@json", SqlDbType.NVarChar, -1).Value = Encoding.UTF8.GetString(RoleActivityFileStore.Serialize(envelope));
            command.ExecuteNonQuery();
        }
        foreach (var code in envelope.Members)
        {
            using var command = new SqlCommand("INSERT dbo.tblApplicationRoleMember(RoleName,EmpCode) VALUES(@role,@code)", connection, transaction);
            command.Parameters.Add("@role", SqlDbType.NVarChar, 20).Value = role;
            command.Parameters.Add("@code", SqlDbType.NVarChar, 20).Value = code;
            command.ExecuteNonQuery();
        }
    }

    private const string SchemaSql = """
        SET XACT_ABORT ON;
        IF (OBJECT_ID(N'dbo.tblApplicationRoleStore') IS NULL AND OBJECT_ID(N'dbo.tblApplicationRoleMember') IS NOT NULL)
           OR (OBJECT_ID(N'dbo.tblApplicationRoleStore') IS NOT NULL AND OBJECT_ID(N'dbo.tblApplicationRoleMember') IS NULL)
            RAISERROR('Access-role schema is partially initialized.',16,1);
        IF OBJECT_ID(N'dbo.tblApplicationRoleStore') IS NULL AND OBJECT_ID(N'dbo.tblApplicationRoleMember') IS NULL
        BEGIN
            CREATE TABLE dbo.tblApplicationRoleStore(
                RoleName nvarchar(20) NOT NULL CONSTRAINT PK_tblApplicationRoleStore PRIMARY KEY,
                Envelope nvarchar(max) NOT NULL,
                CONSTRAINT CK_tblApplicationRoleStore_Role CHECK(RoleName IN(N'Admin',N'Developer')));
            CREATE TABLE dbo.tblApplicationRoleMember(
                RoleName nvarchar(20) NOT NULL,
                EmpCode nvarchar(20) NOT NULL,
                CONSTRAINT PK_tblApplicationRoleMember PRIMARY KEY(RoleName,EmpCode),
                CONSTRAINT FK_tblApplicationRoleMember_Store FOREIGN KEY(RoleName) REFERENCES dbo.tblApplicationRoleStore(RoleName),
                CONSTRAINT CK_tblApplicationRoleMember_Code CHECK(DATALENGTH(EmpCode) BETWEEN 2 AND 40
                    AND EmpCode COLLATE Latin1_General_BIN2 NOT LIKE N'%[^0-9]%'));
        END;
        """;

    private const string ValidateSchemaSql = """
        SELECT CASE WHEN
            OBJECT_ID(N'dbo.tblApplicationRoleStore',N'U') IS NOT NULL
            AND OBJECT_ID(N'dbo.tblApplicationRoleMember',N'U') IS NOT NULL
            AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblApplicationRoleStore'))=2
            AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblApplicationRoleMember'))=2
            AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblApplicationRoleStore')
                AND name=N'RoleName' AND system_type_id=231 AND max_length=40 AND is_nullable=0)
            AND EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblApplicationRoleStore')
                AND name=N'Envelope' AND system_type_id=231 AND max_length=-1 AND is_nullable=0)
            AND (SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.tblApplicationRoleMember')
                AND name IN(N'RoleName',N'EmpCode') AND system_type_id=231 AND max_length=40 AND is_nullable=0)=2
            AND (SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'dbo.tblApplicationRoleStore') AND i.is_primary_key=1 AND c.name=N'RoleName' AND ic.key_ordinal=1)=1
            AND (SELECT COUNT(*) FROM sys.indexes i JOIN sys.index_columns ic ON ic.object_id=i.object_id AND ic.index_id=i.index_id
                JOIN sys.columns c ON c.object_id=ic.object_id AND c.column_id=ic.column_id
                WHERE i.object_id=OBJECT_ID(N'dbo.tblApplicationRoleMember') AND i.is_primary_key=1
                    AND ((c.name=N'RoleName' AND ic.key_ordinal=1) OR (c.name=N'EmpCode' AND ic.key_ordinal=2)))=2
            AND EXISTS(SELECT 1 FROM sys.foreign_keys f JOIN sys.foreign_key_columns k ON k.constraint_object_id=f.object_id
                JOIN sys.columns p ON p.object_id=k.parent_object_id AND p.column_id=k.parent_column_id
                JOIN sys.columns r ON r.object_id=k.referenced_object_id AND r.column_id=k.referenced_column_id
                WHERE f.parent_object_id=OBJECT_ID(N'dbo.tblApplicationRoleMember')
                    AND f.referenced_object_id=OBJECT_ID(N'dbo.tblApplicationRoleStore')
                    AND f.is_disabled=0 AND f.is_not_trusted=0 AND p.name=N'RoleName' AND r.name=N'RoleName')
            THEN 1 ELSE 0 END;
        """;
}
