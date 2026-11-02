using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class AdminOrDeveloperFilter : IAsyncPageFilter
{
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;

    public AdminOrDeveloperFilter(AdminService admins, DeveloperRoleService developers)
    {
        _admins = admins;
        _developers = developers;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var actor = context.HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrWhiteSpace(actor))
        {
            context.Result = new RedirectToPageResult("/Login");
            return;
        }
        if (!_developers.IsDeveloper(actor) && !_admins.IsAdmin(actor))
        {
            context.Result = new StatusCodeResult(StatusCodes.Status403Forbidden);
            return;
        }
        await next();
    }
}
