using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class UpdateUserAccessFilter : IAsyncPageFilter
{
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;

    public UpdateUserAccessFilter(AdminService admins, DeveloperRoleService developers)
    {
        _admins = admins;
        _developers = developers;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var userCode = context.HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(userCode))
        {
            context.Result = new RedirectToPageResult("/Login");
            return;
        }

        var request = context.HttpContext.Request;
        var method = context.HandlerMethod?.MethodInfo;
        var isReadOnlySearch = HttpMethods.IsPost(request.Method) && method != null &&
            method.DeclaringType == typeof(UpdateUserModel) && method.Name == nameof(UpdateUserModel.OnPostAsync) &&
            context.HandlerArguments.TryGetValue("action", out var action) && action is string text && text == "search";
        if (_admins.IsAdmin(userCode) ||
            (_developers.IsDeveloper(userCode) && (HttpMethods.IsGet(request.Method) || isReadOnlySearch)))
        {
            await next();
            return;
        }

        context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
    }
}
