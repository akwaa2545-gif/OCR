using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace OperatorCertificationRecord.Web.Pages
{
    [AdminOnly]
    public class SettingsModel : PageModel
    {
        private readonly EmployeeService _employeeService;
    private readonly IConfiguration _configuration;

    public SettingsModel(EmployeeService employeeService, IConfiguration configuration)
    {
        _employeeService = employeeService;
        _configuration = configuration;
    }

        // Current user info (read-only display)
        public string EmpCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public string DeptName { get; set; } = "";

        // Change-password form fields
        [BindProperty]
        public string CurrentPassword { get; set; } = "";

        [BindProperty]
        public string NewPassword { get; set; } = "";

        [BindProperty]
        public string ConfirmPassword { get; set; } = "";

        [TempData]
        public string? StatusMessage { get; set; }

        [TempData]
        public string? StatusType { get; set; }   // "success" | "error"

        [BindProperty]
        public IFormFile? PhotoFile { get; set; }

        public string PhotoUrl { get; set; } = "";

        public async Task<IActionResult> OnGetAsync()
        {
            EmpCode  = HttpContext.Session.GetString("UserCode") ?? "";
            FullName = HttpContext.Session.GetString("UserName") ?? EmpCode;
            DeptName = HttpContext.Session.GetString("DepartmentName") ?? "";

            if (!string.IsNullOrWhiteSpace(EmpCode))
            {
                try
                {
                    var emp = await _employeeService.GetEmployeeByCodeAsync(EmpCode);
                    if (emp?.PhotoPath != null)
                        PhotoUrl = NormalizePhotoFileName(emp.PhotoPath);
                }
                catch { /* photo is non-critical */ }
            }

            return Page();
        }

        public async Task<IActionResult> OnPostUploadPhotoAsync()
        {
            var empCode = HttpContext.Session.GetString("UserCode") ?? "";
            if (string.IsNullOrWhiteSpace(empCode) || PhotoFile == null || PhotoFile.Length == 0)
            {
                StatusMessage = "Please select a photo file to upload.";
                StatusType = "error";
                return RedirectToPage();
            }

            // reuse upload logic from AddUser/UpdateUser
            string photoPath = "";
            try
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
                if (!Directory.Exists(uploadsFolder))
                    Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{empCode}_{DateTime.Now.Ticks}{Path.GetExtension(PhotoFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await PhotoFile.CopyToAsync(stream);
                }
                var shareRoot = _configuration["PhotoPath"];
                if (!string.IsNullOrWhiteSpace(shareRoot))
                {
                    try
                    {
                        var shareDest = Path.Combine(shareRoot, fileName);
                        var shareDir = Path.GetDirectoryName(shareDest);
                        if (!Directory.Exists(shareDir) && shareDir != null)
                            Directory.CreateDirectory(shareDir);
                        System.IO.File.Copy(filePath, shareDest, true);
                        photoPath = shareDest;
                    }
                    catch { photoPath = $"/uploads/{fileName}"; }
                }
                else
                {
                    photoPath = $"/uploads/{fileName}";
                }

                // update database record
                var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
                if (emp != null)
                {
                    emp.PhotoPath = photoPath;
                    var ok = await _employeeService.UpdateEmployeeAsync(emp);
                    if (ok)
                    {
                        HttpContext.Session.SetString("PhotoPath", photoPath);
                        StatusMessage = "Profile picture updated.";
                        StatusType = "success";
                    }
                    else
                    {
                        StatusMessage = "Failed to save photo path to database.";
                        StatusType = "error";
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error uploading photo: " + ex.Message;
                StatusType = "error";
            }

            return RedirectToPage();
        }

        private string NormalizePhotoFileName(string storedPath)
        {
            if (string.IsNullOrWhiteSpace(storedPath)) return "";
            var normalized = storedPath.Replace('\\', '/');
            return Path.GetFileName(normalized);
        }

        public async Task<IActionResult> OnPostChangePasswordAsync()
        {
            var empCode = HttpContext.Session.GetString("UserCode") ?? "";

            if (string.IsNullOrWhiteSpace(CurrentPassword) ||
                string.IsNullOrWhiteSpace(NewPassword)     ||
                string.IsNullOrWhiteSpace(ConfirmPassword))
            {
                StatusMessage = "All fields are required.";
                StatusType    = "error";
                return RedirectToPage();
            }

            if (NewPassword != ConfirmPassword)
            {
                StatusMessage = "New password and confirmation do not match.";
                StatusType    = "error";
                return RedirectToPage();
            }

            if (NewPassword.Length < 4)
            {
                StatusMessage = "New password must be at least 4 characters.";
                StatusType    = "error";
                return RedirectToPage();
            }

            var ok = await _employeeService.ChangePasswordAsync(empCode, CurrentPassword, NewPassword);

            if (ok)
            {
                StatusMessage = "Password changed successfully.";
                StatusType    = "success";
            }
            else
            {
                StatusMessage = "Current password is incorrect.";
                StatusType    = "error";
            }

            return RedirectToPage();
        }
    }
}
