using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public interface IReadinessProbe
{
    Task CheckAsync(CancellationToken cancellationToken);
}

/// <summary>Read-only connectivity probe; never initializes or migrates the database.</summary>
public sealed class SqlReadinessProbe(IConfiguration configuration) : IReadinessProbe
{
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("Readiness database is not configured.");

        var settings = new SqlConnectionStringBuilder(connectionString)
        {
            ConnectTimeout = 3,
            ConnectRetryCount = 0
        };
        await using var connection = new SqlConnection(settings.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT 1", connection) { CommandTimeout = 3 };
        var result = await command.ExecuteScalarAsync(cancellationToken);
        if (result is not int value || value != 1)
            throw new InvalidOperationException("Readiness query did not succeed.");
    }
}
