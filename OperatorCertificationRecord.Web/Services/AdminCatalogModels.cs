using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed record CatalogWorkshop(string Id, string Name);
public sealed record CatalogProcess(string Key, string? Id, string Name, string? WorkshopId, bool CanMove = true)
{
    public static string CreateKey(string? id, string name, string? workshopId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { id, name, workshopId }))));
}

public sealed record AdminCatalogSnapshot(
    IReadOnlyList<CatalogWorkshop> Workshops,
    IReadOnlyList<CatalogProcess> Processes)
{
    public static AdminCatalogSnapshot Empty { get; } = new([], []);
}

public sealed record WorkshopCatalogInput
{
    public string? NewWorkshopName { get; init; }
}

public sealed record ProcessCatalogInput
{
    public string Mode { get; init; } = "new";
    public string? DestinationWorkshopId { get; init; }
    public string? ExistingProcessKey { get; init; }
    public string? NewProcessName { get; init; }
    public bool ConfirmMove { get; init; }
}

public enum CatalogSaveStatus { Saved, Unchanged, Invalid, Forbidden, Conflict, Unavailable }
public sealed record CatalogSaveResult(CatalogSaveStatus Status, string Message, string? WorkshopId = null, string? ProcessId = null, string? SourceWorkshopId = null)
{
    public bool Success => Status is CatalogSaveStatus.Saved or CatalogSaveStatus.Unchanged;
}

public interface IAdminCatalogService
{
    Task<AdminCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
    Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, CancellationToken cancellationToken = default);
    Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, CancellationToken cancellationToken = default);
}

public interface IAdminCatalogStore
{
    Task<AdminCatalogSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken);
    Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, Func<bool> authorized, CancellationToken cancellationToken);
}
