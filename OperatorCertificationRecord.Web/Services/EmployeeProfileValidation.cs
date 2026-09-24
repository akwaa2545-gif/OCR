using OperatorCertificationRecord.Web.Models;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Protects full-profile saves from clearing required legacy search fields.</summary>
public static class EmployeeProfileValidation
{
    public static IReadOnlyDictionary<string, string> Validate(
        Employee current, string? grade, string? department, string? section, string? workshop,
        IEnumerable<JobGrade> grades, IEnumerable<Department> departments,
        IEnumerable<Section> sections, IEnumerable<Workshop> workshops)
    {
        var errors = new Dictionary<string, string>();
        Check("JobGrade", "Job Grade", grade,
            Same(grade, current.JobGrade) || grades.Any(x => Same(x.JobGradeID, grade)));
        Check("DeptID", "Department", department,
            Same(department, current.DeptID) || departments.Any(x => Same(x.DeptID, department)));
        Check("SectID", "Section", section,
            (Same(department, current.DeptID) && Same(section, current.SectID)) ||
            sections.Any(x => Same(x.SectID, section) && Same(x.DeptID, department)));
        Check("WorkshopID", "Workshop", workshop,
            Same(workshop, current.WorkshopID) || workshops.Any(x => Same(x.WorkshopID, workshop)));
        return errors;

        void Check(string field, string label, string? value, bool allowed)
        {
            if (string.IsNullOrWhiteSpace(value)) errors.Add(field, $"{label} is required. No changes were saved.");
            else if (!allowed) errors.Add(field, $"Select a valid {label}. No changes were saved.");
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
