using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public interface IRoleActivitySink
{
    Task PublishAsync(WebActivityRecord record, CancellationToken ct);
}

public sealed class RoleActivitySqlSink(IConfiguration configuration, IServiceScopeFactory scopes) : IRoleActivitySink
{
    public async Task PublishAsync(WebActivityRecord record, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<WebActivityWriter>();
        using var connection = new SqlConnection(configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();
        await writer.AppendTrustedAsync(connection, transaction, record, ct);
        transaction.Commit();
    }
}

public sealed class RoleActivityDispatcher(RoleActivityJournal journal, IRoleActivitySink sink,
    ILogger<RoleActivityDispatcher> logger) : BackgroundService
{
    public async Task<int> DrainAsync(CancellationToken ct = default)
    {
        var published = 0;
        foreach (var item in journal.ReadPending().Items)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await sink.PublishAsync(item.Record, ct);
                journal.Acknowledge(item);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception error) { journal.ReportFailure(error); throw; }
            published++;
        }
        return published;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DrainAsync(stoppingToken); failures = 0; }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                failures++;
                journal.ReportFailure(error);
                logger.LogWarning("Role audit delivery will retry without dropping saved events.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Pow(2, Math.Min(failures, 4)))), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
