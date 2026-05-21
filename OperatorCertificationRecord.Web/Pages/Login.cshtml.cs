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

    [BindProperty]
    public string Username { get; set; } = "";

    [BindProperty]
    public string Password { get; set; } = "";

    public string ErrorMessage { get; set; } = "";

    public LoginModel(IConfiguration configuration, DepartmentService departmentService, AdminService adminService)
    {
        _configuration = configuration;
        _departmentService = departmentService;
        _adminService = adminService;
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
                            HttpContext.Session.SetString("PhotoPath", reader["Photo"]?.ToString() ?? "");

                            // Get department name
                            var departments = await _departmentService.GetAllDepartmentsAsync();
                            var dept = departments.FirstOrDefault(d => d.DeptID == deptId);
                            HttpContext.Session.SetString("DepartmentName", dept?.DeptName ?? "Unknown");

                            // Admin check
                            if (_adminService.IsAdmin(empCode))
                            {
                                HttpContext.Session.SetString("IsAdmin", "true");
                            }

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
            ErrorMessage = $"An error occurred: {ex.Message}";
            return Page();
        }
    }
}
