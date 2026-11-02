using System;
using System.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace OperatorCertificationRecord.FileStorage
{
    public static class CurrentEmployeePolicy
    {
        public static bool IsCurrent(DateTime? resignDate, string? statusWork) =>
            !resignDate.HasValue
            && !string.Equals(statusWork?.TrimEnd(' '), "0", StringComparison.Ordinal);

        public static async Task EnsureCurrentAsync(
            SqlConnection connection,
            SqlTransaction transaction,
            string employeeCode,
            CancellationToken ct
        )
        {
            using (
                var command = new SqlCommand(
                    "SELECT ResignDate,StatusWork FROM dbo.tblEmployee WITH (UPDLOCK,HOLDLOCK) WHERE EmpCode=@employee",
                    connection,
                    transaction
                )
            )
            {
                command.Parameters.Add(FileValidation.EmployeeParameter(employeeCode));
                using (var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false))
                {
                    if (
                        !await reader.ReadAsync(ct).ConfigureAwait(false)
                        || !IsCurrent(
                            reader.IsDBNull(0) ? (DateTime?)null : reader.GetDateTime(0),
                            reader.IsDBNull(1) ? null : Convert.ToString(reader.GetValue(1))
                        )
                    )
                        throw new InvalidOperationException(
                            "Only current employees may receive new SQL attachments."
                        );
                }
            }
        }
    }
}
