using System.Security.Cryptography;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;

namespace OperatorCertificationRecord.FileMigration;

public static class MigrationJournal
{
    private sealed record Envelope(string Sha256, MigrationManifest Manifest);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public static async Task SaveManifestAsync(string path, MigrationManifest manifest, string workRoot, CancellationToken ct)
    {
        ProtectRoot(workRoot); ValidateTrustedPath(path, workRoot, false);
        if (File.Exists(path)) throw new IOException("Manifest already exists; use a new filename to preserve review history.");
        var envelope = new Envelope(Hash(manifest), manifest);
        await AtomicSave(path, JsonSerializer.Serialize(envelope, Json), ct);
    }
    public static async Task<MigrationManifest> LoadManifestAsync(string path, string workRoot, CancellationToken ct)
    {
        ValidateTrustedPath(path, workRoot);
        var envelope = JsonSerializer.Deserialize<Envelope>(await File.ReadAllTextAsync(path, ct)) ?? throw new InvalidDataException("Empty manifest.");
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(envelope.Sha256), Convert.FromHexString(Hash(envelope.Manifest))))
            throw new InvalidDataException("Manifest integrity check failed.");
        return envelope.Manifest;
    }
    public static async Task<RetainedOwnership[]> LoadRetainedOwnershipAsync(string path, string workRoot, CancellationToken ct)
    {
        ValidateTrustedPath(path, workRoot);
        if (new FileInfo(path).Length > 1_048_576) throw new InvalidDataException("Reviewed ownership input exceeds 1 MiB.");
        return JsonSerializer.Deserialize<RetainedOwnership[]>(await File.ReadAllTextAsync(path, ct))
            ?? throw new InvalidDataException("Empty reviewed ownership input.");
    }
    public static string Hash(MigrationManifest manifest) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manifest)));
    public static FileStream Lock(string workRoot)
    {
        var path = Path.Combine(workRoot, "migration.lock");
        ValidateTrustedPath(path, workRoot, false);
        var stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        try { if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); return stream; }
        catch { stream.Dispose(); throw; }
    }
    public static Task SaveCheckpointAsync(string path, MigrationManifest manifest, ImportResult[] results, string workRoot, CancellationToken ct)
    {
        ValidateTrustedPath(path, workRoot, false);
        return AtomicSave(path, JsonSerializer.Serialize(new { ManifestSha256 = Hash(manifest), UpdatedUtc = DateTime.UtcNow, Results = results }, Json), ct);
    }

    private static async Task AtomicSave(string path, string content, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".partial";
        try
        {
            await File.WriteAllTextAsync(temporary, content, ct);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static void ProtectRoot(string root)
    {
        Directory.CreateDirectory(root); SourceFiles.AssertNoReparse(root);
        if (OperatingSystem.IsWindows())
        {
            var security = new DirectorySecurity(); security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
            new DirectoryInfo(root).SetAccessControl(security);
        }
        else File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
    private static void ValidateProtectedRoot(string root)
    {
        SourceFiles.AssertNoReparse(root);
        if (OperatingSystem.IsWindows())
        {
            var acl = new DirectoryInfo(root).GetAccessControl();
            if (!acl.AreAccessRulesProtected) throw new InvalidDataException("Migration work root must have protected permissions.");
            ValidateWindowsAcl(acl);
        }
        else if ((File.GetUnixFileMode(root) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite | UnixFileMode.OtherRead | UnixFileMode.GroupRead)) != 0)
            throw new InvalidDataException("Migration work root must be private.");
    }
    private static void ValidateTrustedPath(string path, string workRoot, bool mustExist = true)
    {
        SourceFiles.AssertUnderRoots(path, new[] { workRoot });
        ValidateProtectedRoot(workRoot);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workRoot));
        for (var parent = Path.GetDirectoryName(Path.GetFullPath(path)); parent != null && !parent.Equals(root, SourceFiles.PathComparison); parent = Path.GetDirectoryName(parent))
            ValidateObjectPermissions(parent, true);
        if (File.Exists(path)) ValidateObjectPermissions(path, false);
        else if (mustExist) throw new FileNotFoundException("Migration state does not exist.");
    }
    private static void ValidateObjectPermissions(string path, bool directory)
    {
        SourceFiles.AssertNoReparse(path);
        if (OperatingSystem.IsWindows())
            ValidateWindowsAcl(directory ? new DirectoryInfo(path).GetAccessControl() : new FileInfo(path).GetAccessControl());
        else if ((File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite | UnixFileMode.OtherRead | UnixFileMode.GroupRead)) != 0)
            throw new InvalidDataException("Migration state and its directories must be private.");
    }
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ValidateWindowsAcl(FileSystemSecurity acl)
    {
        var current = WindowsIdentity.GetCurrent().User!;
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var owner = acl.GetOwner(typeof(SecurityIdentifier));
        if (!current.Equals(owner) && !system.Equals(owner) && !administrators.Equals(owner))
            throw new InvalidDataException("Migration state has an untrusted owner.");
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && !rule.IdentityReference.Equals(current)
                && !rule.IdentityReference.Equals(system) && !rule.IdentityReference.Equals(administrators))
                throw new InvalidDataException("Migration state grants access to another principal.");
    }
}
