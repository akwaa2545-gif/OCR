using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Models;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class AdminAccessMembersTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ocr-access-members-" + Guid.NewGuid().ToString("N"));
    private string AdminPath => Path.Combine(_directory, "admins.json");
    private string DeveloperPath => Path.Combine(_directory, "developers.json");
    private readonly IConfiguration _configuration;

    public AdminAccessMembersTests()
    {
        Directory.CreateDirectory(_directory);
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AdminRoles:StorePath"] = AdminPath,
            ["DeveloperRoles:StorePath"] = DeveloperPath
        }).Build();
    }

    [Fact]
    public async Task ManagementListsDistinctUnionWithIndependentBadgesAndCounts()
    {
        Seed(new[] { "102", "101", "00101" }, new[] { "103", "101", "00104", "103" });
        var model = CreateModel(new LookupEmployees());

        await model.OnGetAsync();

        Assert.Equal(new[] { "00101", "00104", "101", "102", "103" }, model.AccessMembers.Select(member => member.EmpCode));
        Assert.Equal(3, model.AdminCount);
        Assert.Equal(3, model.DeveloperCount);
        var both = model.AccessMembers.Single(member => member.EmpCode == "101");
        Assert.True(both.IsAdmin); Assert.True(both.IsDeveloper);
        var developerOnly = model.AccessMembers.Single(member => member.EmpCode == "103");
        Assert.False(developerOnly.IsAdmin); Assert.True(developerOnly.IsDeveloper);
        var adminOnly = model.AccessMembers.Single(member => member.EmpCode == "102");
        Assert.True(adminOnly.IsAdmin); Assert.False(adminOnly.IsDeveloper);
        Assert.Equal(new[] { "00101", "101", "102" }, model.AdminsWithNames.Select(member => member.EmpCode));
        Assert.Equal("Preview 00104 Employee", model.AccessMembers.Single(member => member.EmpCode == "00104").FullName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnavailableEmployeeRetainsCodeAndRoleMembership(bool lookupThrows)
    {
        Seed(new[] { "101" }, new[] { "103" });
        var employees = new LookupEmployees { Unavailable = "103", ThrowForUnavailable = lookupThrows };
        var model = CreateModel(employees);

        await model.OnGetAsync();

        var row = model.AccessMembers.Single(member => member.EmpCode == "103");
        Assert.Equal("103", row.FullName);
        Assert.Equal("", row.PhotoUrl);
        Assert.False(row.IsAdmin); Assert.True(row.IsDeveloper);
        Assert.Equal(1, model.AdminCount); Assert.Equal(1, model.DeveloperCount);
    }

    [Fact]
    public void DeveloperListIsSortedDetachedAndFresh()
    {
        Seed(Array.Empty<string>(), new[] { "103", "101", "00101", "103" });
        var roles = CreateDevelopers();
        var snapshot = roles.GetAllDevelopers();
        Assert.Equal(new[] { "00101", "101", "103" }, snapshot);
        var editableCopy = Assert.IsType<string[]>(snapshot);
        editableCopy[0] = "999";
        Assert.False(roles.IsDeveloper("999")); Assert.True(roles.IsDeveloper("00101"));
        File.WriteAllText(DeveloperPath, "[\"104\"]");
        Assert.Equal(new[] { "104" }, roles.GetAllDevelopers());
    }

    [Fact]
    public void MissingOrCorruptDeveloperStoreNeverReturnsPreviousMembers()
    {
        var roles = CreateDevelopers();
        Assert.Empty(roles.GetAllDevelopers());
        Assert.False(File.Exists(DeveloperPath));
        File.WriteAllText(DeveloperPath, "[\"101\"]");
        Assert.Equal(new[] { "101" }, roles.GetAllDevelopers());
        File.WriteAllText(DeveloperPath, "not valid JSON");
        Assert.Empty(roles.GetAllDevelopers());
        Assert.False(roles.IsDeveloper("101"));
    }

    [Fact]
    public void UnreadableDeveloperStoreFailsClosed()
    {
        Seed(Array.Empty<string>(), new[] { "101" });
        var roles = CreateDevelopers();
        using var blocked = new FileStream(DeveloperPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Empty(roles.GetAllDevelopers());
    }

    private void Seed(string[] admins, string[] developers)
    {
        File.WriteAllText(AdminPath, JsonSerializer.Serialize(admins));
        File.WriteAllText(DeveloperPath, JsonSerializer.Serialize(developers));
    }

    private DeveloperRoleService CreateDevelopers() => new(_configuration, NullLogger<DeveloperRoleService>.Instance);

    private AdminsModel CreateModel(LookupEmployees employees)
    {
        var developers = CreateDevelopers();
        return new AdminsModel(new AdminService(_configuration, developers: developers), employees, developers: developers);
    }

    private sealed class LookupEmployees : FakeEmployeeService
    {
        public string? Unavailable { get; set; }
        public bool ThrowForUnavailable { get; set; }

        public override Task<Employee?> GetEmployeeByCodeAsync(string empCode)
        {
            if (empCode == Unavailable)
            {
                if (ThrowForUnavailable) throw new IOException("Synthetic employee lookup failure");
                return Task.FromResult<Employee?>(null);
            }
            return Task.FromResult<Employee?>(new Employee { EmpCode = empCode, FirstNameEng = "Preview " + empCode, LastNameEng = "Employee" });
        }
    }

    public void Dispose()
    {
        foreach (var path in Directory.GetFiles(_directory)) File.Delete(path);
        Directory.Delete(_directory);
    }
}
