using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace OperatorCertificationRecord.Web.Services;

public sealed class BulkEmployeeService
{
    private readonly IBulkEmployeeStore _store;
    private readonly AdminService _admins;
    private readonly DeveloperRoleService _developers;
    private readonly IDataProtector _protector;
    private readonly TimeProvider _clock;
    private readonly ILogger<BulkEmployeeService> _logger;

    public BulkEmployeeService(IBulkEmployeeStore store, AdminService admins, DeveloperRoleService developers,
        IDataProtectionProvider protection, TimeProvider clock, ILogger<BulkEmployeeService> logger)
    {
        _store = store; _admins = admins; _developers = developers;
        _protector = protection.CreateProtector("AdminTools.BulkEmployees.Preview.v1");
        _clock = clock; _logger = logger;
    }

    public async Task<BulkEmployeeSearch> SearchAsync(string actor, string? query, int page, CancellationToken ct)
    {
        if (!_admins.IsAdmin(actor)) throw new UnauthorizedAccessException();
        var search = (query ?? "").Trim();
        if (search.Length > 80 || search.Any(char.IsControl)) throw new ArgumentException("Search must be 80 characters or fewer.");
        return await _store.SearchAsync(search, Math.Clamp(page, 1, 10000), ct);
    }

    public Task<BulkEmployeeChoices> ChoicesAsync(string actor, CancellationToken ct)
    {
        if (!_admins.IsAdmin(actor)) throw new UnauthorizedAccessException();
        return _store.ChoicesAsync(ct);
    }

    public async Task<BulkEmployeeResult> PreviewAsync(string actor, string sessionId, IEnumerable<string>? employeeCodes,
        BulkEmployeePatch input, CancellationToken ct)
    {
        if (!_admins.IsAdmin(actor)) return Failed("Administrator access is required.");
        var rawCodes = employeeCodes?.Take(101).ToArray() ?? [];
        if (rawCodes.Length is < 1 or > 100 || rawCodes.Any(code => !ValidCode(code)))
            return Failed("Select between 1 and 100 valid employees.");
        var codes = rawCodes.Select(code => code.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
        var patch = Clean(input);
        if (!patch.HasChanges || !ValidValue(patch.WorkshopId, 50) || !ValidValue(patch.JobGrade, 50) ||
            (patch.Shift != null && patch.Shift is not ("A" or "B" or "C" or "G")))
            return Failed("Choose at least one valid change.");
        if (string.IsNullOrWhiteSpace(sessionId)) return Failed("Your session expired. Reload this page.");
        var rows = await _store.ReadSelectedAsync(codes, ct);
        if (rows.Count != codes.Length || !rows.Select(row => row.EmpCode).ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(codes))
            return Failed("One or more selected employees could not be found. Reload and select them again.");
        var choices = await _store.ChoicesAsync(ct);
        if (!ValidChoices(patch, choices)) return Failed("A selected workshop or job grade is no longer available.");
        if (!Authorized(actor, codes)) return Failed("Your access changed, or the selection contains a protected Developer employee.");
        if (rows.Any(row => !row.Current)) return Failed("Only current employees can be updated in bulk.");
        if (rows.Any(row => GradeLocked(row, patch))) return Failed("A promoted employee's job grade cannot be changed. Keep the grade unchanged or remove that employee.");
        if (!rows.Any(row => Changes(row, patch))) return Failed("The selected employees already have these values.");
        var destination = patch.WorkshopId == null ? "" : choices.Workshops.Single(choice => choice.Id == patch.WorkshopId).Name;
        var batch = new BulkEmployeeBatch(actor, sessionId, _clock.GetUtcNow().AddMinutes(10), patch, rows, destination, Guid.NewGuid());
        var token = _protector.Protect(JsonSerializer.Serialize(batch));
        return new(true, "Review the changes below before applying them.", new(token, batch));
    }

    public async Task<BulkEmployeeResult> ApplyAsync(string actor, string sessionId, string? token, bool confirmed, CancellationToken ct)
    {
        if (!confirmed) return Failed("Confirm that you reviewed the changes before applying them.");
        if (string.IsNullOrWhiteSpace(token) || token.Length > 180000) return Failed("The preview is invalid. Create a new preview.");
        BulkEmployeeBatch? batch;
        try { batch = JsonSerializer.Deserialize<BulkEmployeeBatch>(_protector.Unprotect(token)); }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { return Failed("The preview is invalid. Create a new preview."); }
        if (batch == null || batch.Actor != actor || batch.SessionId != sessionId || batch.ExpiresAt <= _clock.GetUtcNow() ||
            batch.Rows.Count is < 1 or > 100)
            return Failed("The preview expired or belongs to another session. Create a new preview.");
        var codes = batch.Rows.Select(row => row.EmpCode).ToArray();
        if (!Authorized(actor, codes)) return Failed("Your access changed, or the selection contains a protected Developer employee.");
        try
        {
            var count = await _store.ApplyAsync(batch, () => batch.ExpiresAt > _clock.GetUtcNow() && Authorized(actor, codes), ct);
            _logger.LogInformation("Bulk employee update applied by {Actor}. Employees={Employees}; Workshop={Workshop}; Shift={Shift}; JobGrade={JobGrade}; Before={Before}",
                actor, string.Join(",", codes), batch.Patch.WorkshopId, batch.Patch.Shift, batch.Patch.JobGrade,
                JsonSerializer.Serialize(batch.Rows.Select(row => new { row.EmpCode, row.WorkshopId, row.Shift, row.JobGrade })));
            return new(true, $"Updated {count} {(count == 1 ? "employee" : "employees")} successfully.");
        }
        catch (BulkEmployeeConflictException error) { return Failed(error.Message); }
    }

    private bool Authorized(string actor, IReadOnlyList<string> targets)
    {
        if (!_admins.IsAdmin(actor)) return false;
        var developers = _developers.GetAllDevelopersForReports().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return developers.Contains(actor) || !targets.Any(developers.Contains);
    }

    internal static bool ValidChoices(BulkEmployeePatch patch, BulkEmployeeChoices choices) =>
        (patch.WorkshopId == null || choices.Workshops.Count(choice => choice.Id == patch.WorkshopId) == 1) &&
        (patch.JobGrade == null || choices.Grades.Count(choice => choice.Id == patch.JobGrade) == 1);
    internal static bool GradeLocked(BulkEmployeeRow row, BulkEmployeePatch patch) => row.Promoted && patch.JobGrade != null && !Same(row.JobGrade, patch.JobGrade);
    internal static bool Changes(BulkEmployeeRow row, BulkEmployeePatch patch) =>
        patch.WorkshopId != null && !Same(row.WorkshopId, patch.WorkshopId) ||
        patch.Shift != null && !Same(row.Shift, patch.Shift) || patch.JobGrade != null && !Same(row.JobGrade, patch.JobGrade);
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool ValidCode(string? value) => !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= 10 && value.Trim().All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
    private static bool ValidValue(string? value, int length) => value == null || value.Length <= length && !value.Any(char.IsControl);
    private static BulkEmployeePatch Clean(BulkEmployeePatch value) => new(Null(value.WorkshopId), Null(value.Shift), Null(value.JobGrade));
    private static string? Null(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static BulkEmployeeResult Failed(string message) => new(false, message);
}
