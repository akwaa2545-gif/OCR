using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages.Reports.Automation;

[AdminOnly]
public class IndexModel : PageModel
{
    public void OnGet() { }
}
