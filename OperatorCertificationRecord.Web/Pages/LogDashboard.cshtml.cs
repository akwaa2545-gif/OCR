using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OperatorCertificationRecord.Web.Pages;

public class LogDashboardModel : PageModel
{
    public void OnGet()
    {
        // Check if user is logged in
        if (string.IsNullOrEmpty(HttpContext.Session.GetString("UserCode")))
        {
            Response.Redirect("/Login");
            return;
        }
    }
}
