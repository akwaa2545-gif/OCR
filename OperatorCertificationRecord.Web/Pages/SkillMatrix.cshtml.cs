using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Filters;

namespace OperatorCertificationRecord.Web.Pages;

[AdminOnly]
public class SkillMatrixModel : PageModel
{
    private readonly SectionService _sectionService;
    private readonly WorkshopService _workshopService;

    public List<Section> Sections { get; set; } = new();
    public List<Workshop> Workshops { get; set; } = new();

    public SkillMatrixModel(SectionService sectionService, WorkshopService workshopService)
    {
        _sectionService = sectionService;
        _workshopService = workshopService;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var userCode = HttpContext.Session.GetString("UserCode");
        if (string.IsNullOrEmpty(userCode))
            return RedirectToPage("/Login");

        Sections = await _sectionService.GetAllSectionsAsync();
        Workshops = await _workshopService.GetAllWorkshopsAsync();
        return Page();
    }

}
