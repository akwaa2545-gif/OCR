using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OperatorCertificationRecord.Web.Filters;
using OperatorCertificationRecord.Web.Services;
using System.IO;

namespace OperatorCertificationRecord.Web.Pages
{
    [AdminOnly]
    public class SettingsModel : PageModel
    {
        private readonly EmployeeService _employeeService;
        private readonly EmployeePhotoStorageService _photoStorageService;
        private readonly ILogger<SettingsModel> _logger;

        public SettingsModel(
            EmployeeService employeeService,
            EmployeePhotoStorageService photoStorageService,
            ILogger<SettingsModel> logger)
        {
            _employeeService = employeeService;
            _photoStorageService = photoStorageService;
            _logger = logger;
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

            try
            {
                var emp = await _employeeService.GetEmployeeByCodeAsync(empCode);
                if (emp == null)
                {
                    StatusMessage = "Employee record was not found.";
                    StatusType = "error";
                    return RedirectToPage();
                }

                var storedPhoto = await _photoStorageService.StoreAndCommitAsync(
                    empCode,
                    PhotoFile,
                    async (pendingPhoto, _) =>
                    {
                        emp.PhotoPath = pendingPhoto.DatabasePath;
                        return await _employeeService.UpdateEmployeeAsync(emp);
                    },
                    HttpContext.RequestAborted);
                var photoPath = storedPhoto.DatabasePath;

                HttpContext.Session.SetString("PhotoPath", photoPath);
                StatusMessage = "Profile picture updated.";
                StatusType = "success";
            }
            catch (PhotoUploadValidationException ex)
            {
                StatusMessage = ex.Message;
                StatusType = "error";
            }
            catch (PhotoCompatibilityCopyException)
            {
                StatusMessage = "The photo could not be synchronized with the legacy application.";
                StatusType = "error";
            }
            catch (PhotoPersistenceException)
            {
                StatusMessage = "Failed to save photo path to database.";
                StatusType = "error";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected profile photo upload failure for employee {EmployeeCode}", empCode);
                StatusMessage = "Error uploading photo.";
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
