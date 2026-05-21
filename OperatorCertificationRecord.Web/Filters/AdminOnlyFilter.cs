using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.Web.Filters
{
    public class AdminOnlyFilter : IAsyncPageFilter
    {
        private readonly AdminService _adminService;

        public AdminOnlyFilter(AdminService adminService)
        {
            _adminService = adminService;
        }

        public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
        {
            return Task.CompletedTask;
        }

        public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
        {
            var http = context.HttpContext;
            var userCode = http.Session.GetString("UserCode");

            // Make the AdminService authoritative. If AdminService says the user is an admin,
            // allow and ensure the session flag is set. If not, clear any stale session flag
            // so a removed admin can't keep access while still logged in.
            var isAdmin = !string.IsNullOrEmpty(userCode) && _adminService.IsAdmin(userCode);
            if (isAdmin)
            {
                http.Session.SetString("IsAdmin", "true");
            }
            else
            {
                // clear stale session flag if present
                if (http.Session.GetString("IsAdmin") == "true")
                {
                    http.Session.Remove("IsAdmin");
                }
            }

            if (!isAdmin)
            {
                var req = http.Request;
                var isAjax = req.Headers["X-Requested-With"] == "XMLHttpRequest" || req.Headers["Accept"].ToString().Contains("application/json");
                if (isAjax)
                {
                    context.Result = new JsonResult(new { success = false, message = "Access denied" });
                    return;
                }
                context.Result = new RedirectToPageResult("/AccessDenied");
                return;
            }

            await next();
        }
    }
}
