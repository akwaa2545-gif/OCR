using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class RoleActivityStorageTests
{
    [Fact]
    public void Sql_authority_uses_logical_keys_and_never_checks_the_old_file()
    {
        var storage = Create(new() { ["AccessRoles:Storage"] = "Sql", ["DeveloperRoles:StorePath"] = "missing.json" });
        Assert.Equal("sql:Developer", storage.Resolve("Developer"));
        Assert.Equal("sql:Admin", storage.Resolve("Admin"));
        Assert.Throws<IOException>(() => storage.Exists(storage.Resolve("Developer")));
        Assert.Throws<ArgumentException>(() => storage.Resolve("Other"));
    }

    [Fact]
    public void File_compatibility_preserves_normalization_and_atomic_writes()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "[\"00101\",\"101\",\"101\"]");
            var storage = Create(new() { ["DeveloperRoles:StorePath"] = path });
            var identity = storage.Resolve("Developer");
            Assert.True(storage.Exists(identity));
            Assert.Equal(new[] { "00101", "101" }, storage.Read(identity).Members);
            using (storage.Enter(identity))
                storage.Write(identity, new RoleActivityEnvelope(1, 2, new[] { "202" }, Array.Empty<WebActivityRecord>()));
            Assert.Equal(new[] { "202" }, storage.Read(identity).Members);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".audit.lock");
        }
    }

    [Fact]
    public void Shared_serializer_rejects_invalid_metadata_and_honors_size_limits()
    {
        Assert.Throws<IOException>(() => RoleActivityFileStore.Parse("{\"schemaVersion\":2,\"members\":[],\"pendingAuditEvents\":[]}"));
        Assert.Throws<IOException>(() => RoleActivityFileStore.Parse("[\"101\"]", 2));
        var envelope = new RoleActivityEnvelope(1, 4, new[] { "101" }, Array.Empty<WebActivityRecord>());
        var encoded = RoleActivityFileStore.Serialize(envelope);
        Assert.Equal(4, RoleActivityFileStore.Parse(Encoding.UTF8.GetString(encoded)).MembershipRevision);
        Assert.Throws<IOException>(() => RoleActivityFileStore.Serialize(envelope, 2));
    }

    [Fact]
    public void Unknown_backend_fails_instead_of_silently_using_a_file()
    {
        Assert.Throws<ArgumentException>(() => Create(new() { ["AccessRoles:Storage"] = "Sqli" }));
    }

    private static RoleActivityStorage Create(Dictionary<string, string?> values) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
}
