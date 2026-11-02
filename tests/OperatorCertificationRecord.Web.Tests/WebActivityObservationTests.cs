using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class WebActivityObservationTests
{
    private static WebActivityObservation Request() => new("/UpdateUser", "POST", "OnPostAsync", "update", 200,
        false, false, false, null, true, true);

    [Fact]
    public void Http200DoesNotProveASavedChange()
    {
        Assert.Equal("Unknown", WebActivityObservationRules.Classify(Request())!.Outcome);
        Assert.Equal("Rejected", WebActivityObservationRules.Classify(Request() with { InvalidModel = true })!.Outcome);
        Assert.Equal("Unknown", WebActivityObservationRules.Classify(Request() with { HasError = true })!.Outcome);
        Assert.Null(WebActivityObservationRules.Classify(Request() with { RecordedOutcome = "Committed" }));
    }
    [Fact]
    public void MenuPreviewReportAndDeniedRequestStaySeparateFromChanges()
    {
        Assert.Null(WebActivityObservationRules.Classify(Request() with { Path = "/AdminTools", Method = "GET" }));
        Assert.Equal("Prepared", WebActivityObservationRules.Classify(Request() with { Handler = "OnPostPreviewAsync" })!.Outcome);
        Assert.Equal("Prepared", WebActivityObservationRules.Classify(Request() with { Handler = "OnGetDownloadAsync", Method = "GET" })!.Outcome);
        Assert.Equal("Denied", WebActivityObservationRules.Classify(Request() with { StatusCode = 403 })!.Outcome);
        Assert.Null(WebActivityObservationRules.Classify(Request() with { Path = "/js/site.js", Method = "GET" }));
    }
    [Theory]
    [InlineData("/ViewUser", "GET", "")]
    [InlineData("/DevTools/ActivityLog", "GET", "")]
    [InlineData("/Dashboard", "POST", "search")]
    public void RoutinePageOpensAndEmployeeSearchesDoNotCreateActivityEvents(string path, string method, string action)
    {
        Assert.Null(WebActivityObservationRules.Classify(Request() with { Path = path, Method = method, FormAction = action }));
    }
    [Fact]
    public void LoginUsesIdentityAfterHandlerAndLogoutKeepsTheAuthenticatedActor()
    {
        var login = Request() with { Path = "/Login", SignedInBefore = false, SignedInAfter = true, LoginConfirmed = true };
        Assert.Equal("Succeeded", WebActivityObservationRules.Classify(login)!.Outcome);
        Assert.Equal("Rejected", WebActivityObservationRules.Classify(login with { SignedInAfter = false })!.Outcome);
        Assert.Equal("Rejected", WebActivityObservationRules.Classify(login with { HasError = true, SignedInBefore = true })!.Outcome);
        Assert.Equal("Rejected", WebActivityObservationRules.Classify(login with { LoginConfirmed = false, SignedInBefore = true })!.Outcome);
        Assert.Equal("Succeeded", WebActivityObservationRules.Classify(login with { Path = "/Logout", SignedInBefore = true, SignedInAfter = false })!.Outcome);
    }
    [Fact]
    public void LogoutConfirmationDoesNotClaimSignOutAndFailedRevocationRemainsUnconfirmed()
    {
        var logout = Request() with { Path = "/Logout", SignedInBefore = true };
        Assert.Null(WebActivityObservationRules.Classify(logout with { Method = "GET" }));
        Assert.NotEqual("Succeeded", WebActivityObservationRules.Classify(logout)!.Outcome);
        Assert.Equal("Unknown", WebActivityObservationRules.Classify(logout with { SignedInAfter = false, HasError = true })!.Outcome);
    }
    [Fact]
    public void PartialResultNeverHidesTheCommittedItems()
    {
        Assert.Equal("Partial", WebActivityObservationRules.Classify(Request() with { RecordedOutcome = "Partial" })!.Outcome);
        Assert.Equal("Unknown", WebActivityObservationRules.Classify(Request() with { ExceptionOccurred = true })!.Outcome);
        Assert.Equal("Denied", WebActivityObservationRules.Classify(Request() with { RecordedOutcome = "Denied" })!.Outcome);
    }

    [Fact]
    public void ActualMenusAndReportFilesAreCoveredWithoutLoggingCountPolling()
    {
        Assert.True(WebActivityObservationRules.Covered("/Dashboard"));
        Assert.True(WebActivityObservationRules.Covered("/OperatorTraining"));
        Assert.True(WebActivityObservationRules.Covered("/SkillMatrix"));
        Assert.True(WebActivityFileAccessMiddleware.IsReport("/api/download/skillinventory"));
        Assert.False(WebActivityFileAccessMiddleware.IsReport("/api/download/skillinventory/count"));
        Assert.False(WebActivityFileAccessMiddleware.IsReport("/api/download/meta/disqualified"));
    }
}
