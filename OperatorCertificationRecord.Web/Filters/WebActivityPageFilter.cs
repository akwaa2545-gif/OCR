using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class WebActivityPageFilter(WebActivityCommands commands, WebActivityContextFactory contexts,
    WebActivityAccessRecorder recorder) : IAsyncPageFilter, IOrderedFilter
{
    public int Order => -2000;
    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var http = context.HttpContext;
        if (!WebActivityObservationRules.Covered(http.Request.Path.Value ?? "/")) { await next(); return; }
        var before = TryActor();
        IFormCollection? form = null;
        if (HttpMethods.IsPost(http.Request.Method) && WebActivityCommands.IsAuditedPath(http.Request.Path.Value ?? "") &&
            !http.Request.HasFormContentType && !string.IsNullOrWhiteSpace(http.Session.GetString("UserCode")))
        {
            context.Result = new BadRequestObjectResult("Use the form on this page to save changes.");
            await recorder.RecordAsync(new("attempts", "request", "Rejected", "The form identity was missing."), before);
            return;
        }
        if (HttpMethods.IsPost(http.Request.Method) && http.Request.HasFormContentType)
        {
            form = await http.Request.ReadFormAsync(http.RequestAborted);
            try
            {
                commands.Bind(http, form);
            }
            catch (ArgumentException)
            {
                context.Result = new BadRequestObjectResult("This form expired or changed. Reload the page and try again.");
                await recorder.RecordAsync(new("attempts", "request", "Rejected", "The form identity was rejected."), before);
                return;
            }
        }
        PageHandlerExecutedContext executed;
        try { executed = await next(); }
        catch
        {
            await recorder.RecordAsync(new("attempts", "request", "Unknown", "The request result was not confirmed."), before);
            throw;
        }
        var status = (executed.Result as IStatusCodeActionResult)?.StatusCode ?? http.Response.StatusCode;
        if (executed.Result is RedirectToPageResult redirect && redirect.PageName == "/Login" && before == null)
            status = StatusCodes.Status401Unauthorized;
        var outcome = http.Items[WebActivityContextFactory.OutcomeKey] as string;
        var observation = new WebActivityObservation(http.Request.Path.Value ?? "/", http.Request.Method,
            context.HandlerMethod?.MethodInfo.Name ?? "", form?["action"].ToString().ToLowerInvariant() ?? "",
            status, !context.ModelState.IsValid, PageHasError(context.HandlerInstance), executed.Exception != null,
            outcome, before != null, !string.IsNullOrEmpty(http.Session.GetString("UserCode")), executed.Result is FileResult,
            executed.Result is RedirectToPageResult login && login.PageName is "/Dashboard" or "/Index");
        var result = WebActivityObservationRules.Classify(observation);
        if (result?.Outcome is "Partial" or "Unknown" && http.Items["WebActivity.OutcomeSummary"] is string summary)
            result = result with { Summary = summary };
        if (result != null)
            await recorder.RecordAsync(result, TryActor() ?? before, employee: EmployeeCode(context.HandlerInstance));
    }

    private WebActivityActor? TryActor()
    {
        try { return contexts.Current(); }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static bool PageHasError(object model)
    {
        var type = model.GetType();
        if (type.GetProperty("HasError")?.GetValue(model) is true) return true;
        if (type.GetProperty("ErrorMessage")?.GetValue(model) is string error && !string.IsNullOrWhiteSpace(error)) return true;
        if (type.GetProperty("MessageType")?.GetValue(model) is string value && value == "error") return true;
        return model is PageModel page && page.TempData.Peek("MessageType") is string messageType && messageType == "error";
    }
    private static string? EmployeeCode(object model)
    {
        var value = model.GetType().GetProperty("EmpCode")?.GetValue(model) as string;
        if (model is OperatorCertificationRecord.Web.Pages.ViewUserModel profile)
            value = profile.Employee?.EmpCode ?? value;
        return !string.IsNullOrWhiteSpace(value) && value.Length <= 20 &&
            value.All(character => char.IsAsciiLetterOrDigit(character) || character == '-') ? value : null;
    }
}
