using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class DeveloperOnlyFilter : IAsyncPageFilter
{
    private readonly DeveloperRoleService _roles;

    public DeveloperOnlyFilter(DeveloperRoleService roles) => _roles = roles;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var userCode = context.HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(userCode))
        {
            context.Result = new RedirectToPageResult("/Login");
            return;
        }
        if (!_roles.IsDeveloper(userCode))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }
        await next();
    }
}
