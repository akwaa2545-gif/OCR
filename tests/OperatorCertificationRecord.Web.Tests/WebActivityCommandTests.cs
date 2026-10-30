using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class WebActivityCommandTests
{
    [Fact]
    public void ProtectedCommandSurvivesRetryButIsBoundToActorSessionAndRoute()
    {
        var clock = new Clock();
        var commands = new WebActivityCommands(new EphemeralDataProtectionProvider(), clock);
        var context = Context();
        var token = commands.Create(context, "/UpdateUser?handler=Save");
        context.Request.QueryString = new QueryString("?handler=Save");
        var id = commands.Validate(context, token);
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, commands.Validate(context, token));
        context.Session.SetString("UserCode", "102");
        Assert.Throws<ArgumentException>(() => commands.Validate(context, token));
        context.Session.SetString("UserCode", "101");
        context.Request.Path = "/AddUser";
        Assert.Throws<ArgumentException>(() => commands.Validate(context, token));
        var other = Context();
        other.Request.QueryString = new QueryString("?handler=Save");
        Assert.Throws<ArgumentException>(() => commands.Validate(other, token));
    }

    [Fact]
    public void TamperedExpiredAndDifferentHandlerCommandsAreRejected()
    {
        var clock = new Clock();
        var commands = new WebActivityCommands(new EphemeralDataProtectionProvider(), clock);
        var context = Context();
        var token = commands.Create(context, "/UpdateUser");
        Assert.Throws<ArgumentException>(() => commands.Validate(context, token + "invalid"));
        context.Request.QueryString = new QueryString("?handler=Other");
        Assert.Throws<ArgumentException>(() => commands.Validate(context, token));
        context.Request.QueryString = QueryString.Empty;
        clock.Now = clock.Now.AddHours(3);
        Assert.Throws<ArgumentException>(() => commands.Validate(context, token));
    }

    [Fact]
    public void FingerprintUsesBusinessFieldsAndNeverPasswordOrCapabilityValues()
    {
        static FormCollection Form(string shift, string secret) => new(new Dictionary<string, StringValues>
        {
            ["EmpCode"] = "0012345", ["Shift"] = shift, ["EmpPassword"] = secret,
            ["Password"] = secret, ["PreviewToken"] = secret, ["Input.PdfUploadId"] = secret,
            ["__RequestVerificationToken"] = secret, [WebActivityCommands.FieldName] = secret
        });
        var original = WebActivityCommands.Fingerprint(Form("A", "first-secret"));
        Assert.Equal(original, WebActivityCommands.Fingerprint(Form("A", "different-secret")));
        Assert.NotEqual(original, WebActivityCommands.Fingerprint(Form("B", "first-secret")));
        Assert.DoesNotContain("secret", original);
        Assert.Equal(64, original.Length);
    }

    [Theory]
    [InlineData("WorkshopInput.NewWorkshopName")]
    [InlineData("ProcessInput.NewProcessName")]
    [InlineData("ProcessInput.DestinationWorkshopId")]
    [InlineData("Patch.JobGrade")]
    [InlineData("SelectedCodes")]
    [InlineData("SelectedProcesses")]
    [InlineData("SelectedProcesses[0]")]
    [InlineData("SelectedProcesses.index")]
    [InlineData("SelectedCodes[0]")]
    [InlineData("SkillRowId")]
    [InlineData("EmpCode[]")]
    [InlineData("Input[SourceRowId]")]
    [InlineData("Patch[JobGrade]")]
    [InlineData("[EmpCode]")]
    [InlineData("[Input][SourceRowId]")]
    [InlineData("[SelectedProcesses][0]")]
    [InlineData("[SelectedProcesses][index]")]
    public void FingerprintCoversActualCatalogBulkAndSkillSelectors(string field)
    {
        static FormCollection Form(string key, string value) => new(new Dictionary<string, StringValues> { [key] = value });
        Assert.NotEqual(WebActivityCommands.Fingerprint(Form(field, "A")), WebActivityCommands.Fingerprint(Form(field, "B")));
    }

    [Fact]
    public void AuthenticatedMutationNeedsACommandButReportHandlerOverridesRemainCompatible()
    {
        var context = Context();
        var commands = new WebActivityCommands(new EphemeralDataProtectionProvider(), new Clock());
        Assert.Throws<ArgumentException>(() => commands.Bind(context, new FormCollection(new Dictionary<string, StringValues>())));
        context.Request.Path = "/Reports/CustomReport";
        context.Request.QueryString = new QueryString("?handler=SaveTemplate");
        commands.Bind(context, new FormCollection(new Dictionary<string, StringValues>()));
        Assert.False(commands.SignsForm(context, "?handler=LoadTemplate"));
        Assert.Null(context.Items["WebActivity.CommandId"]);
    }

    [Fact]
    public void QueryBoundMutationFieldsAreFingerprintBoundWithoutPersistingOtherQueryValues()
    {
        var empty = new FormCollection(new Dictionary<string, StringValues>());
        static QueryCollection Query(string employee, string password) => new(new Dictionary<string, StringValues>
        { ["EmpCode"] = employee, ["Password"] = password, ["PreviewToken"] = password });
        var first = WebActivityCommands.RequestFingerprint(empty, Query("0012345", "first-secret"));
        Assert.NotEqual(first, WebActivityCommands.RequestFingerprint(empty, Query("0099999", "first-secret")));
        Assert.Equal(first, WebActivityCommands.RequestFingerprint(empty, Query("0012345", "other-secret")));
    }

    [Fact]
    public void LongNamedCollectionIndicesRemainBoundAndOversizedSelectorsFailClosed()
    {
        var label = new string('x', 201);
        FormCollection Selection(string value) => new(new Dictionary<string, StringValues>
        { ["SelectedProcesses.index"] = label, ["SelectedProcesses[" + label + "]"] = value });
        Assert.NotEqual(WebActivityCommands.Fingerprint(Selection("Process A")), WebActivityCommands.Fingerprint(Selection("Process B")));
        var oversized = new FormCollection(new Dictionary<string, StringValues>
        { ["SelectedProcesses[" + new string('x', 2048) + "]"] = "Process A" });
        Assert.Throws<ArgumentException>(() => WebActivityCommands.Fingerprint(oversized));
        var unsupported = new FormCollection(new Dictionary<string, StringValues>
        { ["[SelectedProcesses][unsupported]"] = "Process A" });
        Assert.Throws<ArgumentException>(() => WebActivityCommands.Fingerprint(unsupported));
    }

    [Theory]
    [InlineData("[Password]")]
    [InlineData("[Input][PdfUploadId]")]
    [InlineData("[PreviewToken]")]
    public void FingerprintIgnoresSensitiveBracketAliases(string field)
    {
        FormCollection Form(string value) => new(new Dictionary<string, StringValues> { [field] = value });
        Assert.Equal(WebActivityCommands.Fingerprint(Form("secret A")), WebActivityCommands.Fingerprint(Form("secret B")));
    }

    private static DefaultHttpContext Context()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<ISessionFeature>(new SessionFeature { Session = new TestSession() });
        context.Session.SetString("UserCode", "101");
        context.Request.Path = "/UpdateUser";
        context.Request.Method = "POST";
        context.Request.Host = new HostString("localhost");
        return context;
    }
    private sealed class SessionFeature : ISessionFeature { public ISession Session { get; set; } = null!; }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 29, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
