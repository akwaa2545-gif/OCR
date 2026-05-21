namespace OperatorCertificationRecord.Web.Models;

public class Employee
{
    public string? EmpCode { get; set; }
    public string? EmpPassword { get; set; }
    public DateTime JoinDate { get; set; }
    public string? JobGrade { get; set; }
    public string? PrefixEng { get; set; }
    public string? FirstNameEng { get; set; }
    public string? LastNameEng { get; set; }
    public string? PrefixThai { get; set; }
    public string? FirstNameThai { get; set; }
    public string? LastNameThai { get; set; }
    public string? DeptID { get; set; }
    public string? SectID { get; set; }
    public string? WorkshopID { get; set; }
    public string? Shift { get; set; }
    public string? PhotoPath { get; set; }
    public string? Notice { get; set; }

    // Resignation fields
    public string? ResignBy { get; set; }
    public DateTime? ResignDate { get; set; }
    public string? StatusWork { get; set; }

    // Transfer fields
    public string? TransferBy { get; set; }
    public DateTime? TransferDate { get; set; }
}

public class Department
{
    public string? DeptID { get; set; }
    public string? DeptName { get; set; }
}

public class Section
{
    public string? SectID { get; set; }
    public string? SectName { get; set; }
    public string? DeptID { get; set; }
}

public class Workshop
{
    public string? WorkshopID { get; set; }
    public string? WorkshopName { get; set; }
}
