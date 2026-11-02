namespace OperatorCertificationRecord.Web.Services;

public static class EmployeeFileUrls
{
    public static string CurrentPhoto(string employee) => $"/api/photo/employee/{Uri.EscapeDataString(employee)}/current";
    public static string Photo(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        if (path.StartsWith("/api/photo/", StringComparison.OrdinalIgnoreCase)) return path;
        var name = Path.GetFileName(path.Replace('\\', '/'));
        return string.IsNullOrWhiteSpace(name) ? "" : "/api/photo/" + Uri.EscapeDataString(name);
    }
    public static string Certificate(string employee, Guid rowId, Guid fileId, bool obsolete = false) =>
        $"/api/certificate/owner/{Uri.EscapeDataString(employee)}/{rowId:D}/{fileId:D}" + (obsolete ? "?obsolete=true" : "");
}
