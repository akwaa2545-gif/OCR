using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data.SqlClient;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.Pages;

public class LoginModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly DepartmentService _departmentService;
    private readonly AdminService _adminService;
    private readonly ILogger<LoginModel>? _logger;

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string ErrorMessage { get; set; } = "";

    public LoginModel(IConfiguration configuration, DepartmentService departmentService, AdminService adminService, ILogger<LoginModel>? logger = null)
    {
        _configuration = configuration;
        _departmentService = departmentService;
        _adminService = adminService;
        _logger = logger;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Username and password are required.";
            return Page();
        }

        try
        {
            var connectionString = _configuration.GetConnectionString("DefaultConnection");
            if (string.IsNullOrEmpty(connectionString))
            {
                ErrorMessage = "Database connection not configured.";
                return Page();
            }

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();
                var query = "SELECT EmpCode, PersonFnameEng, PersonLnameEng, DeptID, Photo FROM tblEmployee WHERE EmpCode = @EmpCode AND EmpPassword = @Password";

                using (var probe = new SqlCommand("SELECT COL_LENGTH('dbo.tblEmployee','PhotoFileId')", connection))
                    if (await probe.ExecuteScalarAsync() is not DBNull)
                        query = query.Replace("SELECT EmpCode", "SELECT PhotoFileId, EmpCode");
                using (var command = new SqlCommand(query, connection))
                {
                    command.Parameters.AddWithValue("@EmpCode", Username);
                    command.Parameters.AddWithValue("@Password", Password);

                    using (var reader = command.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            var empCode = reader["EmpCode"].ToString() ?? "";
                            var firstName = reader["PersonFnameEng"]?.ToString() ?? "";
                            var lastName = reader["PersonLnameEng"]?.ToString() ?? "";
                            var deptId = reader["DeptID"]?.ToString() ?? "";

                            HttpContext.Session.SetString("UserCode", empCode);
                            HttpContext.Session.SetString("UserName", $"{firstName} {lastName}");
                            HttpContext.Session.SetString("DeptID", deptId);
                            HttpContext.Session.SetString("LoginDate", DateTime.Now.ToString("dd MMMM yyyy"));

                            // store photo path for later pages
                            var photoId = Enumerable.Range(0, reader.FieldCount).Any(i => reader.GetName(i) == "PhotoFileId") ? reader["PhotoFileId"] as Guid? : null;
                            var photoPath = photoId.HasValue ? EmployeeFileUrls.CurrentPhoto(empCode) : reader["Photo"]?.ToString() ?? "";
                            HttpContext.Session.SetString("PhotoPath", photoPath);

                            // Get department name
                            var departments = await _departmentService.GetAllDepartmentsAsync();
                            var dept = departments.FirstOrDefault(d => d.DeptID == deptId);
                            HttpContext.Session.SetString("DepartmentName", dept?.DeptName ?? "Unknown");

                            // Admin check
                            HttpContext.Session.SetString("IsAdmin", _adminService.IsAdmin(empCode) ? "true" : "false");

                            return RedirectToPage("/Dashboard");
                        }
                    }
                }
            }

            ErrorMessage = "Invalid username or password.";
            return Page();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Login failed while loading employee data");
            ErrorMessage = "Login is temporarily unavailable. Please try again.";
            return Page();
        }
    }
}
