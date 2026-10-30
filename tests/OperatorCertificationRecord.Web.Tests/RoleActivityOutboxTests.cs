using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class RoleActivityOutboxTests
{
    [Fact]
    public void LegacyArraysUpgradeAtomicallyAndEveryRoleReaderAcceptsTheEnvelope()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Updated, fixture.SetAdmin("101", "00102", true));
        var envelope = fixture.ReadAdmin();
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal(1, envelope.MembershipRevision);
        Assert.Contains("00102", envelope.Members);
        var saved = Assert.Single(envelope.PendingAuditEvents);
        Assert.Equal("admin-enable", saved.Action);
        Assert.Equal("101", saved.Actor.ActorCode);
        Assert.True(saved.Actor.IsAdmin);
        Assert.True(saved.Actor.IsDeveloper);
        Assert.Equal("00102", Assert.Single(saved.Changes).EmployeeCode);
        Assert.True(new AdminService(fixture.Configuration, developers: fixture.Developers).IsAdmin("00102"));
        Assert.Equal(new[] { "101", "103" }, fixture.Developers.GetAllDevelopersForReports());
        var original = File.ReadAllText(fixture.AdminPath);
        Assert.Throws<IOException>(() => new AdminService(fixture.Configuration, developers: fixture.Developers).SetAdmin("101", "104", true));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
    }

    [Fact]
    public void DeveloperRemovalStoresItsAuthenticatedActorBeforeRevocation()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Updated, fixture.SetDeveloper("101", "101", false));
        Assert.False(fixture.Developers.IsDeveloper("101"));
        var saved = Assert.Single(fixture.ReadDeveloper().PendingAuditEvents);
        Assert.Equal("developer-remove", saved.Action);
        Assert.Equal("101", saved.Actor.ActorCode);
        Assert.True(saved.Actor.IsDeveloper);
        Assert.True(saved.Actor.IsAdmin);
        Assert.Equal("true", Assert.Single(Assert.Single(saved.Changes).Fields).Before);
        Assert.Equal("false", Assert.Single(Assert.Single(saved.Changes).Fields).After);
        Assert.Equal(new[] { "103" }, fixture.Developers.GetAllDevelopersForReports());
    }

    [Fact]
    public void SpoofedActorCannotPublishMembershipOrAnOutboxEvent()
    {
        using var fixture = new Fixture();
        fixture.Http.Session.SetString("UserCode", "103");
        var original = File.ReadAllText(fixture.AdminPath);
        Assert.Throws<UnauthorizedAccessException>(() => fixture.SetAdmin("101", "104", true));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
        Assert.Empty(fixture.ReadAdmin().PendingAuditEvents);
    }

    [Fact]
    public void FullEventQueueRejectsTheNextChangeWithoutLosingExistingHistory()
    {
        using var fixture = new Fixture(maxPending: 1);
        Assert.Equal(RoleChangeResult.Updated, fixture.SetAdmin("101", "102", true));
        var saved = File.ReadAllText(fixture.AdminPath);
        Assert.Throws<IOException>(() => fixture.SetAdmin("101", "104", true));
        Assert.Equal(saved, File.ReadAllText(fixture.AdminPath));
        Assert.True(fixture.Admins.IsAdmin("102"));
        Assert.False(fixture.Admins.IsAdmin("104"));
        Assert.Single(fixture.ReadAdmin().PendingAuditEvents);
    }

    [Fact]
    public void ByteLimitAndFailedAtomicPublicationLeaveMembershipUntouched()
    {
        using var bounded = new Fixture(maxBytes: 128);
        var original = File.ReadAllText(bounded.AdminPath);
        Assert.Throws<IOException>(() => bounded.SetAdmin("101", "102", true));
        Assert.Equal(original, File.ReadAllText(bounded.AdminPath));
        using var failing = new Fixture(publish: (_, _) => throw new IOException("Synthetic publication failure"));
        original = File.ReadAllText(failing.AdminPath);
        Assert.Throws<IOException>(() => failing.SetAdmin("101", "102", true));
        Assert.Equal(original, File.ReadAllText(failing.AdminPath));
        Assert.Empty(Directory.GetFiles(failing.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task SqlFailureRetainsTheSavedRoleAndDurablePendingEvent()
    {
        using var fixture = new Fixture();
        fixture.SetAdmin("101", "102", true);
        var sink = new Sink { Failure = new IOException("Synthetic SQL failure with private details") };
        var dispatcher = fixture.Dispatcher(sink);
        await Assert.ThrowsAsync<IOException>(() => dispatcher.DrainAsync());
        Assert.True(fixture.Admins.IsAdmin("102"));
        Assert.Single(fixture.ReadAdmin().PendingAuditEvents);
        Assert.Equal(1, fixture.Journal.GetHealth().PendingCount);
        Assert.DoesNotContain("private", fixture.Journal.GetHealth().LastError!, StringComparison.OrdinalIgnoreCase);
        sink.Failure = null;
        Assert.Equal(1, await dispatcher.DrainAsync());
        Assert.Empty(fixture.ReadAdmin().PendingAuditEvents);
        Assert.NotNull(fixture.Journal.GetHealth().LastDeliveryUtc);
    }

    [Fact]
    public async Task SqlCommittedBeforeAcknowledgementRetriesWithoutDuplicatingTheEvent()
    {
        var failAck = false;
        using var fixture = new Fixture(publish: (temporary, path) =>
        {
            if (failAck) throw new IOException("Synthetic crash before acknowledgement");
            File.Move(temporary, path, overwrite: true);
        });
        fixture.SetAdmin("101", "102", true);
        var sink = new Sink();
        var dispatcher = fixture.Dispatcher(sink);
        failAck = true;
        await Assert.ThrowsAsync<IOException>(() => dispatcher.DrainAsync());
        Assert.Single(sink.Stored);
        Assert.Single(fixture.ReadAdmin().PendingAuditEvents);
        failAck = false;
        Assert.Equal(1, await dispatcher.DrainAsync());
        Assert.Equal(2, sink.Deliveries);
        Assert.Single(sink.Stored);
        Assert.Empty(fixture.ReadAdmin().PendingAuditEvents);
        Assert.True(fixture.Admins.IsAdmin("102"));
    }

    [Fact]
    public async Task AcknowledgementPreservesMembershipAndEventsAddedAfterDeliveryStarted()
    {
        using var fixture = new Fixture();
        fixture.SetAdmin("101", "102", true);
        var sink = new Sink { BeforePublish = () => fixture.SetAdmin("101", "104", true) };
        var dispatcher = fixture.Dispatcher(sink);
        Assert.Equal(1, await dispatcher.DrainAsync());
        Assert.True(fixture.Admins.IsAdmin("102"));
        Assert.True(fixture.Admins.IsAdmin("104"));
        Assert.Equal("104", Assert.Single(Assert.Single(fixture.ReadAdmin().PendingAuditEvents).Changes).EmployeeCode);
        sink.BeforePublish = null;
        Assert.Equal(1, await dispatcher.DrainAsync());
        Assert.Equal(2, sink.Stored.Count);
        Assert.Empty(fixture.ReadAdmin().PendingAuditEvents);
    }

    [Fact]
    public async Task RoleRevocationDoesNotDiscardAnEarlierTrustedActorsQueuedHistory()
    {
        using var fixture = new Fixture();
        fixture.SetDeveloper("101", "104", true);
        fixture.Http.Session.SetString("UserCode", "103");
        fixture.SetDeveloper("103", "101", false);
        Assert.False(fixture.Developers.IsDeveloper("101"));
        var sink = new Sink();
        Assert.Equal(2, await fixture.Dispatcher(sink).DrainAsync());
        Assert.Contains(sink.Stored.Values, record => record.Actor.ActorCode == "101" && record.Actor.IsDeveloper == true);
        Assert.Empty(fixture.ReadDeveloper().PendingAuditEvents);
    }

    [Fact]
    public async Task SeparateServiceInstancesSerializeConcurrentRoleChangesWithoutLostUpdates()
    {
        using var fixture = new Fixture();
        var other = fixture.AnotherAdminService();
        var results = await Task.WhenAll(Task.Run(() => fixture.SetAdmin("101", "102", true)),
            Task.Run(() => other.SetAdmin("101", "104", true)));
        Assert.All(results, result => Assert.Equal(RoleChangeResult.Updated, result));
        Assert.Equal(new[] { "101", "102", "104" }, fixture.ReadAdmin().Members);
        Assert.Equal(2, fixture.ReadAdmin().PendingAuditEvents.Length);
    }

    [Fact]
    public void CorruptEnvelopeFailsClosedAndNoOpOrLastMemberDoesNotInventSavedChanges()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Unchanged, fixture.SetAdmin("101", "101", true));
        Assert.Equal(RoleChangeResult.LastMember, fixture.SetAdmin("101", "101", false));
        Assert.Empty(fixture.ReadAdmin().PendingAuditEvents);
        fixture.SetAdmin("101", "102", true);
        File.WriteAllText(fixture.AdminPath, File.ReadAllText(fixture.AdminPath).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 9"));
        Assert.False(fixture.Admins.IsAdmin("101"));
        Assert.Equal(RoleChangeResult.Forbidden, fixture.SetAdmin("101", "104", true));
        Assert.Null(fixture.Journal.GetHealth().PendingCount);
    }

    [Fact]
    public void ReplayedGrantAfterAnInterveningRevokeReturnsItsPriorResultWithoutChangingMembership()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Updated, fixture.SetAdmin("101", "102", true));
        var command = fixture.Http.Items["WebActivity.CommandId"];
        fixture.SetAdmin("101", "102", false);
        fixture.Http.Items["WebActivity.CommandId"] = command;
        var original = File.ReadAllText(fixture.AdminPath);
        Assert.Equal(RoleChangeResult.Updated, fixture.Admins.SetAdmin("101", "102", true));
        Assert.False(fixture.Admins.IsAdmin("102"));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
        Assert.Equal(2, fixture.ReadAdmin().ConsumedCommands!.Length);
    }

    [Fact]
    public void AConsumedCommandCannotChangeItsTargetRoleDesiredStateOrFormHash()
    {
        using var fixture = new Fixture();
        fixture.SetAdmin("101", "102", true);
        var originalAdmin = File.ReadAllText(fixture.AdminPath);
        var originalDeveloper = File.ReadAllText(fixture.DeveloperPath);
        Assert.Throws<ArgumentException>(() => fixture.Admins.SetAdmin("101", "104", true));
        Assert.Throws<ArgumentException>(() => fixture.Admins.SetAdmin("101", "102", false));
        Assert.Throws<ArgumentException>(() => fixture.Developers.SetDeveloper("101", "102", true));
        fixture.Http.Items["WebActivity.CommandFingerprint"] = new string('B', 64);
        Assert.Throws<ArgumentException>(() => fixture.Admins.SetAdmin("101", "102", true));
        Assert.Equal(originalAdmin, File.ReadAllText(fixture.AdminPath));
        Assert.Equal(originalDeveloper, File.ReadAllText(fixture.DeveloperPath));
    }

    [Fact]
    public async Task DispatcherAcknowledgementKeepsTheConsumedCommandAfterItsAuditEventIsDelivered()
    {
        using var fixture = new Fixture();
        fixture.SetDeveloper("101", "104", true);
        var command = fixture.Http.Items["WebActivity.CommandId"];
        Assert.Equal(1, await fixture.Dispatcher(new Sink()).DrainAsync());
        Assert.Empty(fixture.ReadDeveloper().PendingAuditEvents);
        Assert.Single(fixture.ReadDeveloper().ConsumedCommands!);
        fixture.SetDeveloper("101", "104", false);
        fixture.Http.Items["WebActivity.CommandId"] = command;
        var original = File.ReadAllText(fixture.DeveloperPath);
        Assert.Equal(RoleChangeResult.Updated, fixture.Developers.SetDeveloper("101", "104", true));
        Assert.False(fixture.Developers.IsDeveloper("104"));
        Assert.Equal(original, File.ReadAllText(fixture.DeveloperPath));
    }

    [Fact]
    public void NoChangeAndFinalHolderCommandsAreConsumedBeforeMembershipCanChangeLater()
    {
        using var fixture = new Fixture();
        Assert.Equal(RoleChangeResult.Unchanged, fixture.SetAdmin("101", "102", false));
        var noChangeCommand = fixture.Http.Items["WebActivity.CommandId"];
        Assert.Equal(RoleChangeResult.LastMember, fixture.SetAdmin("101", "101", false));
        var lastMemberCommand = fixture.Http.Items["WebActivity.CommandId"];
        fixture.SetAdmin("101", "102", true);
        var original = File.ReadAllText(fixture.AdminPath);
        fixture.Http.Items["WebActivity.CommandId"] = noChangeCommand;
        Assert.Equal(RoleChangeResult.Unchanged, fixture.Admins.SetAdmin("101", "102", false));
        fixture.Http.Items["WebActivity.CommandId"] = lastMemberCommand;
        Assert.Equal(RoleChangeResult.LastMember, fixture.Admins.SetAdmin("101", "101", false));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
    }

    [Fact]
    public async Task FullUnexpiredReceiptStoreRejectsNewCommandsEvenAfterQueueDelivery()
    {
        using var fixture = new Fixture(maxReceipts: 1);
        fixture.SetAdmin("101", "102", true);
        await fixture.Dispatcher(new Sink()).DrainAsync();
        var original = File.ReadAllText(fixture.AdminPath);
        Assert.Throws<IOException>(() => fixture.SetAdmin("101", "104", true));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
        Assert.False(fixture.Admins.IsAdmin("104"));
    }

    [Fact]
    public void MissingValidatedCommandIdentityCannotChangeAccess()
    {
        using var fixture = new Fixture();
        fixture.Http.Items.Remove("WebActivity.CommandId");
        var original = File.ReadAllText(fixture.AdminPath);
        Assert.Throws<ArgumentException>(() => fixture.Admins.SetAdmin("101", "102", true));
        Assert.Equal(original, File.ReadAllText(fixture.AdminPath));
    }

    [Fact]
    public void ReceiptCapacityIsReclaimedOnlyAfterTheCommandValidityWindow()
    {
        var clock = new MutableClock();
        using var fixture = new Fixture(maxReceipts: 1, clock: clock);
        fixture.SetAdmin("101", "102", true);
        clock.Now = clock.Now.AddHours(2).AddTicks(-1);
        Assert.Throws<IOException>(() => fixture.SetAdmin("101", "104", true));
        clock.Now = clock.Now.AddTicks(2);
        Assert.Equal(RoleChangeResult.Updated, fixture.SetAdmin("101", "104", true));
        Assert.Single(fixture.ReadAdmin().ConsumedCommands!);
        Assert.Equal(2, fixture.ReadAdmin().PendingAuditEvents.Length);
    }

    private sealed class Fixture : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ocr-role-outbox-" + Guid.NewGuid().ToString("N"));
        public string AdminPath => Path.Combine(DirectoryPath, "admins.json");
        public string DeveloperPath => Path.Combine(DirectoryPath, "developers.json");
        public IConfiguration Configuration { get; }
        public DefaultHttpContext Http { get; }
        public RoleActivityJournal Journal { get; }
        public DeveloperRoleService Developers { get; }
        public AdminService Admins { get; }

        public Fixture(int maxPending = 1000, int maxBytes = 5 * 1024 * 1024, Action<string, string>? publish = null,
            int maxReceipts = 10000, TimeProvider? clock = null)
        {
            Directory.CreateDirectory(DirectoryPath);
            File.WriteAllText(AdminPath, "[\"101\"]");
            File.WriteAllText(DeveloperPath, "[\"101\",\"103\"]");
            Configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["AdminRoles:StorePath"] = AdminPath, ["DeveloperRoles:StorePath"] = DeveloperPath }).Build();
            Http = CreateHttp();
            Journal = new RoleActivityJournal(Configuration, new WebActivityContextFactory(new Accessor { HttpContext = Http }),
                clock ?? TimeProvider.System, NullLogger<RoleActivityJournal>.Instance, maxPending, maxBytes, publish, maxReceipts);
            Developers = new DeveloperRoleService(Configuration, NullLogger<DeveloperRoleService>.Instance, Journal);
            Admins = new AdminService(Configuration, developers: Developers, activity: Journal);
        }

        private static DefaultHttpContext CreateHttp()
        {
            var http = new DefaultHttpContext();
            http.Features.Set<ISessionFeature>(new SessionFeature());
            http.Session.SetString("UserCode", "101");
            http.Request.Path = "/AccessRole";
            NewCommand(http);
            return http;
        }
        private static void NewCommand(HttpContext http)
        {
            http.Items["WebActivity.CommandId"] = Guid.NewGuid();
            http.Items["WebActivity.CommandFingerprint"] = new string('A', 64);
        }
        public RoleChangeResult SetAdmin(string actor, string target, bool enabled)
        {
            NewCommand(Http);
            return Admins.SetAdmin(actor, target, enabled);
        }
        public RoleChangeResult SetDeveloper(string actor, string target, bool enabled)
        {
            NewCommand(Http);
            return Developers.SetDeveloper(actor, target, enabled);
        }
        public AdminService AnotherAdminService()
        {
            var journal = new RoleActivityJournal(Configuration, new WebActivityContextFactory(new Accessor { HttpContext = CreateHttp() }),
                TimeProvider.System, NullLogger<RoleActivityJournal>.Instance);
            return new AdminService(Configuration, developers: new DeveloperRoleService(Configuration,
                NullLogger<DeveloperRoleService>.Instance, journal), activity: journal);
        }
        public RoleActivityEnvelope ReadAdmin() => RoleActivityFileStore.Read(AdminPath);
        public RoleActivityEnvelope ReadDeveloper() => RoleActivityFileStore.Read(DeveloperPath);
        public RoleActivityDispatcher Dispatcher(IRoleActivitySink sink) => new(Journal, sink, NullLogger<RoleActivityDispatcher>.Instance);
        public void Dispose() { if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, recursive: true); }
    }

    private sealed class Accessor : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = new TestSession(); }
    private sealed class Sink : IRoleActivitySink
    {
        public Dictionary<Guid, WebActivityRecord> Stored { get; } = new();
        public int Deliveries { get; private set; }
        public Exception? Failure { get; set; }
        public Action? BeforePublish { get; set; }
        public Task PublishAsync(WebActivityRecord record, CancellationToken ct)
        {
            Deliveries++;
            if (Failure != null) throw Failure;
            BeforePublish?.Invoke();
            Stored.TryAdd(record.EventUid, record);
            return Task.CompletedTask;
        }
    }
}
