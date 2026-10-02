using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.Web.Tests.Fakes;

public class FakeEmployeeService : EmployeeService
{
    public bool PromotedResult { get; set; } = false;
    public bool ResignedResult { get; set; } = false;
    public bool PromotionArchiveResult { get; set; } = true;
    public bool PromotionArchiveCalled { get; private set; }
    public string? PromotionArchiveEmpCode { get; private set; }
    public string? PromotionArchiveNextGrade { get; private set; }
    public string? PromotionArchivePerformedBy { get; private set; }
    public CancellationToken PromotionArchiveCancellationToken { get; private set; }
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

    public override Task<bool> PromoteEmployeeAndArchiveSkillsAsync(
        string empCode,
        string nextGrade,
        string performedBy,
        CancellationToken cancellationToken = default)
    {
        PromotionArchiveCalled = true;
        PromotionArchiveEmpCode = empCode;
        PromotionArchiveNextGrade = nextGrade;
        PromotionArchivePerformedBy = performedBy;
        PromotionArchiveCancellationToken = cancellationToken;
        return Task.FromResult(PromotionArchiveResult);
    }
}
