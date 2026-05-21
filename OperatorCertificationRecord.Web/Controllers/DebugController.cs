using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DebugController : ControllerBase
    {
        private readonly IConfiguration _config;
        private readonly Services.EmployeeService _employeeService;

        public DebugController(IConfiguration config, Services.EmployeeService employeeService)
        {
            _config = config;
            _employeeService = employeeService;
        }

        // GET /api/debug/expiry
        [HttpGet("expiry")]
        public async Task<IActionResult> ExpiryDebug()
        {
            var startDate = DateTime.Today;
            var endDate = DateTime.Today.AddDays(30).Date.AddDays(1).AddTicks(-1);
            var connStr = _config.GetConnectionString("DefaultConnection") ?? _config["ConnectionStrings:DefaultConnection"];
            if (string.IsNullOrWhiteSpace(connStr)) return StatusCode(500, new { error = "Database connection string is not configured." });

            try
            {
                var sqlView = @"SELECT TOP (500) EmpCode, ProcessName, Remark, ResignDate, DisQualifiedBy, ExpiryDate
                        FROM ViewEmpQualified_All
                        WHERE ExpiryDate BETWEEN @start AND @end
                          AND (ISNULL(DisQualifiedBy,'') = '')
                          AND ProcessName IS NOT NULL
                        ORDER BY ExpiryDate ASC";

                var viewRows = new List<object>();
                using (var conn = new SqlConnection(connStr))
                using (var cmd = new SqlCommand(sqlView, conn))
                {
                    cmd.Parameters.Add("@start", SqlDbType.DateTime).Value = startDate;
                    cmd.Parameters.Add("@end", SqlDbType.DateTime).Value = endDate;
                    conn.Open();
                    using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        viewRows.Add(new {
                            EmpCode = reader["EmpCode"]?.ToString(),
                            Process = reader["ProcessName"]?.ToString(),
                            Remark = reader["Remark"]?.ToString(),
                            ResignDate = reader["ResignDate"] as DateTime?,
                            DisQualifiedBy = reader["DisQualifiedBy"]?.ToString(),
                            ExpiryDate = reader["ExpiryDate"] as DateTime?
                        });
                    }
                }

                var svcList = await _employeeService.GetExpiringSkillsAsync(30);
                var svcSamples = svcList?.Take(30).Select(i => new { i.EmpCode, i.Process, i.ExpiryDate, i.DeptName, i.SectName, i.FirstNameEng, i.LastNameEng }).Cast<object>().ToList() ?? new List<object>();

                return Ok(new {
                    viewCount = viewRows.Count,
                    serviceCount = svcList?.Count ?? 0,
                    viewSamples = viewRows.Take(30),
                    serviceSamples = svcSamples
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
