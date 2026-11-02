using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services.Authentication;

internal static class LoginAccountSql
{
    internal const string ActiveEmployee = "e.ResignDate IS NULL AND ISNULL(LTRIM(RTRIM(CONVERT(nvarchar(20),e.StatusWork))),N'')<>N'0' AND e.EmpPassword IS NOT NULL AND DATALENGTH(CONVERT(nvarchar(max),e.EmpPassword))>0";
    internal const string FromEmployee = " FROM dbo.tblEmployee e LEFT JOIN dbo.tblDepartment d ON d.DeptID=e.DeptID ";

    internal static async Task<string> ColumnsAsync(SqlConnection connection, SqlTransaction? transaction, CancellationToken ct)
    {
        using var probe = new SqlCommand("SELECT COL_LENGTH(N'dbo.tblEmployee',N'PhotoFileId')", connection, transaction);
        var column = await probe.ExecuteScalarAsync(ct);
        var photoId = column is not null and not DBNull ? "e.PhotoFileId" : "CAST(NULL AS uniqueidentifier)";
        return "e.EmpCode,e.PersonFnameEng,e.PersonLnameEng,e.DeptID,ISNULL(d.DeptName,N'Unknown'),e.Photo," + photoId +
            ",HASHBYTES('SHA2_256',CONVERT(nvarchar(max),e.EmpPassword))";
    }

    internal static LoginAccount? Read(SqlDataReader reader)
    {
        if (reader.IsDBNull(7)) return null;
        var stamp = (byte[])reader.GetValue(7);
        if (stamp.Length != 32) return null;
        var code = reader.GetString(0);
        var name = string.Join(" ", new[] { Text(reader, 1).Trim(), Text(reader, 2).Trim() }.Where(value => value.Length > 0));
        var photo = reader.IsDBNull(6) ? (reader.GetValue(5) as string ?? "") : EmployeeFileUrls.CurrentPhoto(code);
        return new(code, name, Text(reader, 3), Text(reader, 4), photo, Convert.ToHexString(stamp));
    }

    internal static bool ValidCode(string? code) => code is { Length: > 0 and <= 20 } &&
        code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    private static string Text(SqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? "" : reader.GetString(ordinal);
}
