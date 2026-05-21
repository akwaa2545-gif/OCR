using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OperatorCertificationRecord.Web.Pages
{
    public class AccessDeniedModel : PageModel
    {
        public string? ReturnUrl { get; private set; }

        public void OnGet()
        {
            // capture referer when possible to allow 'Go back' behavior
            ReturnUrl = Request.Headers["Referer"].ToString();
        }
    }
}
