using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.Web.Tests.Fakes;

public class FakeEmployeeService : EmployeeService
{
    public bool PromotedResult { get; set; } = false;
    public bool ResignedResult { get; set; } = false;
    public Employee? StubEmployee { get; set; }
    public List<EmployeeSkillRecord> StubCurrentSkills { get; set; } = new();

    public FakeEmployeeService() : base(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string> { { "ConnectionStrings:DefaultConnection", string.Empty } }).Build())
    {
    }

    public override Task<bool> IsEmployeePromotedAsync(string empCode)
    {
        return Task.FromResult(PromotedResult);
    }

    public override Task<bool> IsEmployeeResignedAsync(string empCode)
    {
        return Task.FromResult(ResignedResult);
    }

    public override Task<Employee?> GetEmployeeByCodeAsync(string empCode)
    {
        return Task.FromResult(StubEmployee);
    }

    public override Task<List<EmployeeSkillRecord>> GetCurrentSkillRecordsAsync(string empCode)
    {
        return Task.FromResult(StubCurrentSkills);
    }
}