using System.Data.SqlClient;

namespace OperatorCertificationRecord.Web.Services;

public sealed class AdminCatalogService : IAdminCatalogService
{
    private readonly IAdminCatalogStore _store;
    private readonly AdminService _admins;
    private readonly ILogger<AdminCatalogService> _logger;

    public AdminCatalogService(IAdminCatalogStore store, AdminService admins, ILogger<AdminCatalogService> logger)
    {
        _store = store;
        _admins = admins;
        _logger = logger;
    }

    public Task<AdminCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => _store.ReadAsync(cancellationToken);

    public Task<CatalogSaveResult> SaveWorkshopAsync(string actor, WorkshopCatalogInput input, CancellationToken cancellationToken = default)
    {
        if (!_admins.IsAdmin(actor)) return Task.FromResult(Forbidden());
        if (!ValidName(input.NewWorkshopName, 50)) return Task.FromResult(Invalid("Enter a workshop name of 1–50 characters."));
        var clean = input with { NewWorkshopName = input.NewWorkshopName!.Trim() };
        return SaveAsync(actor, "workshop", () => _store.SaveWorkshopAsync(actor.Trim(), clean, () => _admins.IsAdmin(actor), cancellationToken), cancellationToken);
    }

    public Task<CatalogSaveResult> SaveProcessAsync(string actor, ProcessCatalogInput input, CancellationToken cancellationToken = default)
    {
        if (!_admins.IsAdmin(actor)) return Task.FromResult(Forbidden());
        if (input.Mode is not ("new" or "move")) return Task.FromResult(Invalid("Choose a new or existing process."));
        if (!ValidId(input.DestinationWorkshopId, 50)) return Task.FromResult(Invalid("Choose the destination workshop."));
        if (input.Mode == "new" && !ValidName(input.NewProcessName, 100)) return Task.FromResult(Invalid("Enter a process name of 1–100 characters."));
        if (input.Mode == "move" && (input.ExistingProcessKey?.Length != 64 || !input.ExistingProcessKey.All(Uri.IsHexDigit)))
            return Task.FromResult(Invalid("Choose an existing process."));
        if (input.Mode == "move" && !input.ConfirmMove) return Task.FromResult(Invalid("Confirm the process move before saving."));
        var clean = input with { DestinationWorkshopId = input.DestinationWorkshopId!.Trim(), NewProcessName = input.NewProcessName?.Trim() };
        return SaveAsync(actor, "process", () => _store.SaveProcessAsync(actor.Trim(), clean, () => _admins.IsAdmin(actor), cancellationToken), cancellationToken);
    }

    private async Task<CatalogSaveResult> SaveAsync(string actor, string kind, Func<Task<CatalogSaveResult>> save, CancellationToken cancellationToken)
    {
        try
        {
            var result = await save();
            if (result.Success) _logger.LogInformation("Admin catalog {Kind} saved by {Actor}; Status={Status}, Workshop={Workshop}, Process={Process}, PreviousWorkshop={PreviousWorkshop}", kind, actor, result.Status, result.WorkshopId, result.ProcessId, result.SourceWorkshopId);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is SqlException or IOException or InvalidOperationException)
        {
            _logger.LogError(error, "Admin catalog {Kind} could not be saved by {Actor}", kind, actor);
            return new(CatalogSaveStatus.Unavailable, "The catalog could not be saved. Please try again.");
        }
    }

    private static bool ValidId(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum && !value.Any(char.IsControl);
    private static bool ValidName(string? value, int maximum) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximum && !value.Any(char.IsControl);
    private static CatalogSaveResult Invalid(string message) => new(CatalogSaveStatus.Invalid, message);
    internal static CatalogSaveResult Forbidden() => new(CatalogSaveStatus.Forbidden, "Admin access is required to change the catalog.");
}
