namespace OperatorCertificationRecord.Web.Services;

public sealed class MissingFileService : IMissingFileService
{
    private readonly AdminService _admins;
    private readonly IMissingFileStore _store;
    private readonly IMissingFileProbe _probe;
    private readonly ILogger<MissingFileService> _logger;
    public MissingFileService(AdminService admins, IMissingFileStore store, IMissingFileProbe probe, ILogger<MissingFileService> logger)
        => (_admins, _store, _probe, _logger) = (admins, store, probe, logger);

    public async Task<MissingFilePage> ScanAsync(string actor, MissingFileQuery query, CancellationToken cancellationToken)
    {
        if (!_admins.IsAdmin(actor)) throw new UnauthorizedAccessException("Admin access required.");
        if (query.Type is not ("all" or "photo" or "certificate") || query.Page is < 1 or > 10000 ||
            query.PageSize is < 1 or > 50 || (query.Search?.Length ?? 0) > 100 || query.Search?.Any(char.IsControl) == true)
            throw new ArgumentException("Choose a valid file type, search and page.");
        var clean = query with { Search = query.Search?.Trim() };
        try
        {
            var candidates = await _store.ReadAsync(clean, cancellationToken);
            if (candidates.Rows.Count > clean.PageSize) throw new InvalidOperationException("The file check exceeded its page limit.");
            var rows = new List<MissingFileResult>(candidates.Rows.Count);
            foreach (var file in candidates.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var check = await _probe.CheckAsync(file, cancellationToken);
                var code = Uri.EscapeDataString(file.EmployeeCode);
                var repairUrl = file.CanRepair
                    ? file.Kind == "Photo" ? "/UpdateUser?empCode=" + code : "/UpdateSkill?empCode=" + code + "&processName=" + Uri.EscapeDataString(file.ProcessName ?? "")
                    : "/ViewUser?empCode=" + code;
                rows.Add(new(file, check.Health, check.Detail, check.PreviewUrl, repairUrl,
                    file.CanRepair ? file.Kind == "Photo" ? "Replace photo" : "Replace PDF" : "Review profile"));
            }
            if (!_admins.IsAdmin(actor)) throw new UnauthorizedAccessException("Admin access required.");
            return new(rows, candidates.HasMore, clean);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (UnauthorizedAccessException) { throw; }
        catch (Exception error)
        {
            _logger.LogError(error, "Missing file checks failed for Admin {Actor}, page {Page}", actor, clean.Page);
            throw new InvalidOperationException("File checks are unavailable. Check storage access and try again.", error);
        }
    }
}
