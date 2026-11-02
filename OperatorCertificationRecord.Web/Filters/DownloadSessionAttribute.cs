using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace OperatorCertificationRecord.Web.Filters;

/// <summary>Reports follow the signed-in employee access policy, without requiring an elevated role.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DownloadSessionAttribute : Attribute, IAuthorizationFilter
{
    private readonly bool _api;

    public DownloadSessionAttribute(bool api = false) => _api = api;

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (!string.IsNullOrWhiteSpace(context.HttpContext.Session.GetString("UserCode"))) return;
        context.Result = _api ? new UnauthorizedResult() : new RedirectToPageResult("/Login");
    }
}
