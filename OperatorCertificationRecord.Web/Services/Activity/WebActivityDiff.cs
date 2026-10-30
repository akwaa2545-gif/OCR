using System.Globalization;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Explicit safe business fields. Unknown keys fail closed rather than serializing form models.</summary>
public static class WebActivityDiff
{
    internal static readonly HashSet<string> AllowedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "EmpCode", "HEng", "PersonFnameEng", "PersonLnameEng", "HThai", "PersonFnameThai", "PersonLnameThai",
        "JoinDate", "JobGrade", "DeptID", "SectID", "WorkshopID", "Shift", "StatusWork", "ResignBy", "ResignDate",
        "TransferBy", "TransferDate", "Notice", "PhotoFileId", "PhotoFileName", "PhotoByteLength", "PhotoSha256",
        "ProcessName", "ProcessID", "WorkshopName", "ProcessKey", "OperatorTraining", "TheoryTraining", "OJTTraining",
        "CertifiedDate", "FullScore", "ActualScore", "TestResult", "JudgmentTheory", "KnowledgeScore", "KnowledgeLevel",
        "SkillScore", "SkillLevel", "JudgmentPractice", "ExpiryDate", "Verifier", "VerifierDate", "DisQualifiedDate",
        "DisQualifiedBy", "TheReason", "Remark", "PromotionId", "PromotedAt", "PromotedBy", "CertificateFileId",
        "PdfFileName", "PdfByteLength", "PdfSha256", "Admin", "Developer", "Role", "Enabled", "Membership",
        "TargetEmployeeCode", "PasswordChanged", "Kind", "FileId", "FileName", "OriginalFileName", "ByteLength", "Sha256"
    };

    public static IReadOnlyList<WebActivityChange> Compare(IReadOnlyList<WebActivityEntitySnapshot> before,
        IReadOnlyList<WebActivityEntitySnapshot> after, bool passwordChanged = false)
    {
        var old = Index(before); var current = Index(after);
        var changes = new List<WebActivityChange>();
        foreach (var key in old.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal))
        {
            old.TryGetValue(key, out var a); current.TryGetValue(key, out var b);
            var entity = b ?? a!;
            var fields = (a?.Fields.Keys ?? []).Concat(b?.Fields.Keys ?? []).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).Select(field =>
                {
                    var prior = a != null && a.Fields.TryGetValue(field, out var av) ? av : null;
                    var next = b != null && b.Fields.TryGetValue(field, out var bv) ? bv : null;
                    return new WebActivityFieldChange(field, prior, next);
                }).Where(field => field.Before != field.After).ToList();
            if (passwordChanged && entity.EntityType.Equals("Employee", StringComparison.OrdinalIgnoreCase))
                fields.Add(new("PasswordChanged", null, null, true));
            if (fields.Count == 0 && a != null && b != null) continue;
            changes.Add(new(entity.EntityType, entity.EntityKey, entity.EmployeeCode, entity.SkillName,
                entity.TableKind, entity.Label, a != null, b != null, fields));
        }
        return changes;
    }

    public static void ValidateChange(WebActivityChange change)
    {
        Check(change.EntityType, 40, false); Check(change.EntityKey, 300, false); Check(change.Label, 500, true);
        Check(change.EmployeeCode, 20, true); Check(change.SkillName, 255, true); Check(change.TableKind, 30, true);
        if (change.Fields.Count > 100) throw new ArgumentException("Activity details exceed the field limit.");
        foreach (var field in change.Fields)
        {
            if (!AllowedFields.Contains(field.Field)) throw new ArgumentException("Activity field is not permitted: " + field.Field);
            if (field.Field.Equals("PasswordChanged", StringComparison.OrdinalIgnoreCase))
            {
                if (!field.Redacted || field.Before != null || field.After != null)
                    throw new ArgumentException("Password activity must contain only a redacted change marker.");
            }
            else
            {
                Check(field.Before, 16384, true, true); Check(field.After, 16384, true, true);
                if ((field.Field.EndsWith("FileName", StringComparison.OrdinalIgnoreCase) || field.Field == "OriginalFileName") &&
                    new[] { field.Before, field.After }.Any(value => value != null && (value.Contains('/') || value.Contains('\\') || value.Contains(':'))))
                    throw new ArgumentException("Activity filenames must not contain storage paths.");
            }
        }
    }

    internal static string? Scalar(object? value) => value switch
    {
        null or DBNull => null, DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset date => date.ToString("O", CultureInfo.InvariantCulture),
        Guid id => id.ToString("D"), byte[] => null,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture), _ => value.ToString()
    };

    internal static string? FileName(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text)) return null;
        return text.Replace('\\', '/').Split('/').Last().Split(':').Last();
    }

    internal static void Check(string? value, int max, bool nullable, bool multiline = false)
    {
        if (value == null && nullable) return;
        if (string.IsNullOrWhiteSpace(value) && !nullable || value?.Length > max ||
            value?.Any(character => char.IsControl(character) && !(multiline && character is '\r' or '\n' or '\t')) == true)
            throw new ArgumentException("An activity value is invalid or too long.");
    }

    private static Dictionary<string, WebActivityEntitySnapshot> Index(IReadOnlyList<WebActivityEntitySnapshot> rows)
    {
        var result = new Dictionary<string, WebActivityEntitySnapshot>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = row.EntityType + "\n" + row.TableKind + "\n" + row.EntityKey;
            ValidateChange(new(row.EntityType, row.EntityKey, row.EmployeeCode, row.SkillName,
                row.TableKind, row.Label, true, true, row.Fields.Select(field => new WebActivityFieldChange(field.Key, null, field.Value)).ToArray()));
            var canonical = row.Fields.ToDictionary(field => AllowedFields.Single(allowed =>
                allowed.Equals(field.Key, StringComparison.OrdinalIgnoreCase)), field => field.Value, StringComparer.OrdinalIgnoreCase);
            if (!result.TryAdd(key, row with { Fields = canonical })) throw new ArgumentException("Activity entity identity is ambiguous.");
        }
        return result;
    }
}
