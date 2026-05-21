using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.Web.Pages.Reports
{
    public class ScheduledExportsModel : PageModel
    {
        public List<Job> Jobs { get; set; } = new List<Job>();

        public async Task OnGetAsync()
        {
            var file = Path.Combine(Directory.GetCurrentDirectory(), "App_Data", "scheduled_exports.json");
            if (System.IO.File.Exists(file))
            {
                try { Jobs = JsonSerializer.Deserialize<List<Job>>(await System.IO.File.ReadAllTextAsync(file)) ?? new List<Job>(); } catch { Jobs = new List<Job>(); }
            }
        }

        public class Job
        {
            public string? Id { get; set; }
            public string? ReportType { get; set; }
            public string? Format { get; set; }
            public string? Section { get; set; }
            public string? Department { get; set; }
            public string? StartDate { get; set; }
            public string? EndDate { get; set; }
            public System.DateTime CreatedAt { get; set; }
        }
    }
}
