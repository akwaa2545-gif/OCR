using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Short-lived, actor/session/form-bound command identities; never audit credentials.</summary>
public sealed class WebActivityCommands(IDataProtectionProvider protection, TimeProvider clock)
{
    public const string FieldName = "__activityCommand";
    private readonly IDataProtector _protector = protection.CreateProtector("WebActivity.Command.v1");
    private sealed record Command(Guid Id, string Actor, string SessionBinding, string Target, DateTimeOffset Expires);
    private static readonly HashSet<string> SafeFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "action", "EmpCode", "JobGrade", "PrefixEng", "FirstNameEng", "LastNameEng", "PrefixThai", "FirstNameThai", "LastNameThai",
        "DeptID", "SectID", "WorkshopID", "Shift", "JoinDate", "Notice", "roleKind", "accessEnabled", "NewEmpCode",
        "Transfer_DeptID", "Transfer_SectID", "Transfer_WorkshopID", "Transfer_Shift", "Reason", "process", "processes",
        "SelectedProcesses", "BulkReason", "BulkSubReason", "SelectedCodes", "PastedCodes", "Patch.WorkshopId", "Patch.Shift", "Patch.JobGrade",
        "Input.EmployeeCode", "Input.SourceTable", "Input.SourceRowId", "Input.Action", "Input.TargetEmployeeCode",
        "Input.PriorRowId", "Input.TargetTab", "Input.Reason", "Input.ReplaceConflict", "UndoActionId", "UndoReason",
        "Input.EmpCode", "Input.ProcessName", "Input.OperatorTraining", "Input.TheoryTraining", "Input.OJTTraining",
        "Input.CertifiedDate", "Input.ExpiryDate", "Input.FullScore", "Input.ActualScore", "Input.TestResult",
        "Input.KnowledgeScore", "Input.KnowledgeLevel", "Input.SkillScore", "Input.SkillLevel", "Input.Verifier", "Input.Remark",
        "Skill_ProcessName", "Skill_OperatorTraining", "Skill_TheoryTraining", "Skill_OJTTraining", "Skill_CertifiedDate",
        "Skill_ExpiryDate", "Skill_FullScore", "Skill_ActualScore", "Skill_TestResult", "Skill_KnowledgeScore",
        "Skill_KnowledgeLevel", "Skill_SkillScore", "Skill_SkillLevel", "Skill_Verifier", "Skill_Remark", "Skill_JudgmentPractice",
        "Skill_WorkshopID", "SkillRowId", "SkillFileStorageVersion",
        "WorkshopInput.NewWorkshopName", "ProcessInput.Mode", "ProcessInput.DestinationWorkshopId", "ProcessInput.ExistingProcessKey",
        "ProcessInput.NewProcessName", "ProcessInput.ConfirmMove"
    };
    private static readonly string[] CollectionFields = ["SelectedProcesses", "SelectedCodes", "processes", "process"];

    public string Create(HttpContext http, string? action)
    {
        var actor = http.Session.GetString("UserCode") ?? throw Invalid();
        WebActivityContextFactory.ValidateActor(actor);
        return _protector.Protect(JsonSerializer.Serialize(new Command(Guid.NewGuid(), actor,
            Binding(http.Session.Id), Target(http, action), clock.GetUtcNow().AddHours(2))));
    }

    public Guid Validate(HttpContext http, string token)
    {
        try
        {
            if (token.Length > 8192) throw Invalid();
            var command = JsonSerializer.Deserialize<Command>(_protector.Unprotect(token)) ?? throw Invalid();
            if (command.Id == Guid.Empty || command.Actor != http.Session.GetString("UserCode") ||
                command.SessionBinding != Binding(http.Session.Id) || command.Expires <= clock.GetUtcNow() ||
                !string.Equals(command.Target, Target(http, null), StringComparison.OrdinalIgnoreCase)) throw Invalid();
            return command.Id;
        }
        catch (Exception error) when (error is CryptographicException or JsonException or FormatException)
        { throw Invalid(); }
    }

    public static bool IsAuditedPath(string path) => new[]
    {
        "/AddUser", "/UpdateUser", "/AddSkill", "/UpdateSkill", "/ViewUser", "/Settings", "/Admins",
        "/AccessRole", "/AdminTools", "/DevTools/SkillRecovery"
    }.Any(prefix => path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));

    public bool SignsForm(HttpContext http, string? action) => IsAuditedPath(Target(http, action).Split('|')[0]);

    public void Bind(HttpContext http, IFormCollection form)
    {
        if (IsAuditedPath(http.Request.Path.Value ?? ""))
        {
            var token = form[FieldName];
            if (token.Count > 1 || !string.IsNullOrWhiteSpace(http.Session.GetString("UserCode")) &&
                (token.Count != 1 || string.IsNullOrWhiteSpace(token[0]))) throw Invalid();
            if (token.Count == 1 && !string.IsNullOrWhiteSpace(token[0]))
                http.Items["WebActivity.CommandId"] = Validate(http, token[0]!);
        }
        http.Items["WebActivity.CommandFingerprint"] = RequestFingerprint(form, http.Request.Query);
    }

    public static string Fingerprint(IFormCollection form) => FingerprintFields(form);

    public static string RequestFingerprint(IFormCollection form, IQueryCollection query) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[]
            { FingerprintFields(form), FingerprintFields(query) }))));

    private static string FingerprintFields(IEnumerable<KeyValuePair<string, Microsoft.Extensions.Primitives.StringValues>> fields)
    {
        var safe = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var item in fields)
        {
            if (item.Key.Length > 2048) throw new ArgumentException("A form selector is too long.");
            var normalizedKey = NormalizeJQueryKey(item.Key);
            if ((IsCollectionSelector(item.Key) || IsCollectionSelector(normalizedKey)) && !IsSafeField(item.Key))
                throw new ArgumentException("A collection selector is invalid or too long.");
            if (!IsSafeField(item.Key)) continue;
            var values = item.Value.Select(value => value ?? "").ToArray();
            if (values.Any(value => value.Length > 32768)) throw new ArgumentException("A form field is too long.");
            safe[item.Key.ToUpperInvariant()] = values;
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(safe))));
    }

    private static bool IsSafeField(string key) => IsSafeCanonicalField(key) || IsSafeCanonicalField(NormalizeJQueryKey(key));

    private static bool IsCollectionSelector(string key) => CollectionFields.Any(root =>
        key.StartsWith(root + "[", StringComparison.OrdinalIgnoreCase) || key.StartsWith(root + ".", StringComparison.OrdinalIgnoreCase));

    private static bool IsSafeCanonicalField(string key) => SafeFields.Contains(key) ||
        CollectionFields.Any(root =>
            key.StartsWith(root + "[", StringComparison.OrdinalIgnoreCase) && key.EndsWith(']') ||
            key.Equals(root + ".index", StringComparison.OrdinalIgnoreCase));

    private static string NormalizeJQueryKey(string key)
    {
        var result = new StringBuilder(key.Length);
        for (var position = 0; position < key.Length; position++)
        {
            if (key[position] != '[') { result.Append(key[position]); continue; }
            var close = key.IndexOf(']', position + 1);
            if (close < 0) return key;
            var part = key[(position + 1)..close];
            if (part.Length > 0)
            {
                if (char.IsDigit(part[0])) result.Append('[').Append(part).Append(']');
                else
                {
                    if (result.Length > 0) result.Append('.');
                    result.Append(part);
                }
            }
            position = close;
        }
        return result.ToString();
    }

    private static string Target(HttpContext http, string? action)
    {
        var value = string.IsNullOrWhiteSpace(action) ? $"{http.Request.PathBase}{http.Request.Path}{http.Request.QueryString}" : action;
        if (Uri.TryCreate(value, UriKind.Absolute, out var absolute) && absolute.Scheme is "http" or "https")
        {
            if (!string.Equals(absolute.Authority, http.Request.Host.Value, StringComparison.OrdinalIgnoreCase)) throw Invalid();
            value = absolute.PathAndQuery;
        }
        var split = value.IndexOf('?');
        var path = split < 0 ? value : value[..split];
        if (path.Length == 0) path = http.Request.PathBase + http.Request.Path;
        if (!path.StartsWith('/')) throw Invalid();
        var handler = split < 0 ? "" : QueryHelpers.ParseQuery(value[split..]).GetValueOrDefault("handler").ToString();
        return path.TrimEnd('/') + "|" + handler;
    }
    private static string Binding(string session) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(session)));
    private static ArgumentException Invalid() => new("This form expired or changed. Reload the page and try again.");
}
