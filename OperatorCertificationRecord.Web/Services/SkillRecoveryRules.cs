namespace OperatorCertificationRecord.Web.Services;

public static class SkillRecoveryRules
{
    public const int DisqualificationReasonLimit = 300;
    public static string Label(string action) => action switch
    {
        "reinstate" => "Reinstate", "restore" => "Restore", "move" => "Move",
        "remove-pdf" => "Remove PDF", "replace-pdf" => "Replace PDF",
        "undo-edit" => "Undo Edit", "undo-add" => "Undo Add", "move-tab" => "Move Tab", "undo" => "Undo", _ => "Recovery"
    };

    public static bool ValidCode(string? code) => !string.IsNullOrWhiteSpace(code) && code.Length <= 10 &&
        code == code.Trim() && code.All(character => char.IsAsciiLetterOrDigit(character) || character == '-');

    public static string? ValidateRequest(SkillRecoveryRequest request)
    {
        if (request.Action is not ("reinstate" or "restore" or "move" or "undo-edit" or "undo-add" or "move-tab" or "undo" or "remove-pdf" or "replace-pdf"))
            return "Choose a valid recovery action.";
        if (!ValidCode(request.EmployeeCode) || !Enum.IsDefined(request.SourceTable)) return "Choose a valid employee and skill version.";
        var reasonError = ValidateReason(request.Reason);
        if (reasonError != null) return reasonError;
        if (request.Action == "undo")
            return !request.ReversesActionId.HasValue || request.ReversesActionId.Value == Guid.Empty
                ? "Choose a recorded recovery to undo." : null;
        if (request.SourceRowId == Guid.Empty) return "Choose an exact skill version.";
        if (request.Action == "replace-pdf" && (!request.PdfUploadId.HasValue || request.PdfUploadId == Guid.Empty))
            return "Choose a PDF to replace this attachment.";
        if (request.Action == "move-tab" && request.TargetTab is not ("current" or "disqualified" or "obsolete"))
            return "Choose Current, Disqualified or Obsolete.";
        if (request.Action == "move-tab" && !string.IsNullOrWhiteSpace(request.TargetEmployeeCode))
            return "Move Tab keeps the skill with the selected employee.";
        if (request.Action == "move-tab" && request.TargetTab == "disqualified" && (request.Reason?.Length ?? 0) > DisqualificationReasonLimit)
            return "Use a reason of at most 300 characters when moving to Disqualified.";
        if (request.Action == "move" && (!ValidCode(request.TargetEmployeeCode) || Same(request.EmployeeCode, request.TargetEmployeeCode)))
            return "Choose a different destination employee.";
        if (request.Action == "undo-edit" && (!request.PriorRowId.HasValue || request.PriorRowId.Value == Guid.Empty))
            return "Choose the previous version to restore.";
        return null;
    }

    internal static string? ValidateReason(string? reason) =>
        (reason?.Length ?? 0) > 500 || reason?.Any(character => char.IsControl(character) && character is not ('\r' or '\n' or '\t')) == true
            ? "Use at most 500 characters without unsupported control characters for the reason." : null;

    public static string? Validate(SkillRecoveryRequest request, SkillRecoveryContext context, bool requireConflictConsent = true, DateTime? now = null)
    {
        var inputError = ValidateRequest(request);
        if (inputError != null) return inputError;
        if (!Same(context.SourceEmployee.EmployeeCode, request.EmployeeCode) || !context.SourceEmployee.Current)
            return "Recovery requires a current source employee. Reload and review the employee.";
        if (request.Action == "undo")
            return context.UndoAllowed && !string.IsNullOrWhiteSpace(context.UndoPayload) ? null :
                context.UndoUnavailableReason ?? "This recovery cannot be undone. The records may have changed or it was already reversed.";

        var source = context.Source;
        if (source == null || !Matches(source, request.EmployeeCode, request.SourceTable, request.SourceRowId))
            return "The selected skill version changed or could not be found. Select it again.";
        if (string.IsNullOrWhiteSpace(source.ProcessName))
            return "This record has no process name and needs review before recovery.";
        if (IsPdfAction(request.Action))
        {
            if (request.Action == "remove-pdf" && !source.Values.CertificateFileId.HasValue && string.IsNullOrWhiteSpace(source.Values.Download))
                return "This skill has no linked PDF to remove.";
            if (request.Action == "replace-pdf")
            {
                var pdf = context.ReplacementPdf;
                if (pdf == null || pdf.UploadId != request.PdfUploadId || pdf.ByteLength < 5 ||
                    string.IsNullOrWhiteSpace(pdf.FileName) || pdf.FileName.Length > 255 ||
                    string.IsNullOrWhiteSpace(pdf.Sha256) || pdf.Sha256.Length != 64 ||
                    !pdf.Sha256.All(char.IsAsciiHexDigit) || pdf.ExpiresAt <= new DateTimeOffset(now ?? DateTime.Now))
                    return "The uploaded PDF expired or changed. Choose the PDF again.";
            }
            return null;
        }
        if (IsPromotion(source)) return "Promotion records require a separate promotion review.";
        if (request.Action == "move-tab")
        {
            if (source.Table == SkillRecoveryTable.Archive && request.TargetTab == "obsolete")
                return "This version is already obsolete.";
            if (source.Table == SkillRecoveryTable.Current && request.TargetTab == "current" && !IsDisqualified(source))
                return "This version is already active. Dates and results determine whether it appears in Current.";
            if (source.Table == SkillRecoveryTable.Current && request.TargetTab == "disqualified" && IsDisqualified(source))
                return "This version is already disqualified.";
            if (request.TargetTab == "current" && Status(source with
                { Table = SkillRecoveryTable.Current, Values = ResultValues(request, source.Values, "Developer", now ?? DateTime.Now) }, now ?? DateTime.Now) != "Current")
                return "This version cannot appear in Current. Review the certificate dates, results and employee status first.";
            // Marking or archiving one exact row must not replace unrelated active versions.
            if (request.TargetTab is "disqualified" or "obsolete") return null;
        }
        if (request.Action == "reinstate" && (source.Table != SkillRecoveryTable.Current || !IsDisqualified(source)))
            return "Choose a disqualified operational skill to reinstate.";
        if (request.Action == "restore" && source.Table != SkillRecoveryTable.Archive)
            return "Choose an obsolete version to restore.";
        if (request.Action == "undo-edit" && source.Table != SkillRecoveryTable.Current)
            return "This entry is already archived. Choose the operational version.";
        if (request.Action == "undo-edit")
        {
            var prior = context.Prior;
            if (prior == null || !Matches(prior, source.EmployeeCode, SkillRecoveryTable.Archive, request.PriorRowId!.Value) ||
                !Same(prior.ProcessName, source.ProcessName) || IsPromotion(prior))
                return "Choose a previous archived version of this employee's same process.";
        }
        var destinationCode = source.EmployeeCode;
        if (request.Action == "move")
        {
            if (context.TargetEmployee == null || !Same(context.TargetEmployee.EmployeeCode, request.TargetEmployeeCode) || !context.TargetEmployee.Current)
                return "Choose a current destination employee.";
            destinationCode = context.TargetEmployee.EmployeeCode;
        }
        if (request.Action == "undo-add") return null;
        if (context.Conflicts.Count > 1 || (request.Action == "undo-edit" && !IsDisqualified(source) && context.Conflicts.Count > 0))
            return "Several operational versions exist for this process. Review them before recovery.";
        if (context.Conflicts.Any(row => row.Table != SkillRecoveryTable.Current || !Same(row.EmployeeCode, destinationCode) ||
            !Same(row.ProcessName, source.ProcessName) || IsDisqualified(row) || IsPromotion(row) || !ValidVersion(row.Version)))
            return "A conflicting version needs review before recovery.";
        if (requireConflictConsent && context.Conflicts.Count > 0 && !request.ReplaceConflict)
            return "Review the existing version and select Replace to archive it before recovery.";
        return null;
    }

    public static bool IsDisqualified(SkillRecoveryRow row) => !string.IsNullOrWhiteSpace(row.Values.DisQualifiedBy);
    public static bool ShouldReplaceConflicts(SkillRecoveryRequest request) =>
        request.Action is not ("undo-add" or "undo") && !IsPdfAction(request.Action) && (request.Action != "move-tab" || request.TargetTab == "current");

    public static bool IsPdfAction(string? action) => action is "remove-pdf" or "replace-pdf";

    public static string TabLabel(string? tab) => tab switch
    {
        "current" => "Current", "disqualified" => "Disqualified", "obsolete" => "Obsolete", _ => "Select tab"
    };

    public static SkillRecoveryValues ResultValues(SkillRecoveryRequest request, SkillRecoveryValues source,
        string actor, DateTime appliedAt)
    {
        if (request.Action == "reinstate" || (request.Action == "move-tab" && request.TargetTab == "current"))
            return source with { DisQualifiedBy = null, DisQualifiedDate = null, TheReason = null };
        if (request.Action == "move-tab" && request.TargetTab == "disqualified")
            return source with { DisQualifiedBy = actor, DisQualifiedDate = appliedAt, TheReason = request.Reason ?? "" };
        return source;
    }
    public static bool IsPromotion(SkillRecoveryRow row) => row.Values.PromotionId.HasValue || row.Values.PromotedAt.HasValue ||
        !string.IsNullOrWhiteSpace(row.Values.PromotedBy) || Contains(row.Values.Remark, "PROMOTED");

    public static string Status(SkillRecoveryRow row, DateTime now)
    {
        if (IsPromotion(row)) return "Promoted";
        if (Contains(row.Values.Remark, "RESIGNED")) return "Resigned";
        if (row.Table == SkillRecoveryTable.Archive) return "Obsolete";
        if (IsDisqualified(row)) return "Disqualified";
        if (!Same(row.Values.JudgmentPractice, "Pass")) return "Failed";
        if (!row.Values.ExpiryDate.HasValue) return "No expiry";
        return row.Values.ExpiryDate.Value >= now ? "Current" : "Expired";
    }

    public static string Outcome(SkillRecoveryRequest request, SkillRecoveryContext context, DateTime now, string actor = "Developer")
    {
        if (request.Action == "undo") return "The recorded recovery will be reversed only if its affected records are unchanged.";
        if (request.Action == "remove-pdf") return "The linked PDF will be removed. Skill details stay unchanged; the original PDF remains in History for Undo.";
        if (request.Action == "replace-pdf") return "The linked PDF will be replaced with " + context.ReplacementPdf?.FileName + ". Skill details stay unchanged; the original PDF remains in History for Undo.";
        if (request.Action == "undo-add") return "The selected entry will be removed. Its original details and PDF remain in History for Undo.";
        if (request.Action == "move-tab" && request.TargetTab == "obsolete")
            return "Result for " + context.SourceEmployee.Name + " (" + context.SourceEmployee.EmployeeCode + "): Obsolete. Only the selected entry will be archived; its history and PDF remain available.";
        var row = request.Action == "undo-edit" ? context.Prior : context.Source;
        if (row == null) return "Select a skill version to preview its outcome.";
        var restored = row with { Table = SkillRecoveryTable.Current, Values = ResultValues(request, row.Values, actor, now) };
        var status = Status(restored, now);
        var destination = request.Action == "move" ? context.TargetEmployee?.Name + " (" + context.TargetEmployee?.EmployeeCode + ")" :
            context.SourceEmployee.Name + " (" + context.SourceEmployee.EmployeeCode + ")";
        return "Result for " + destination + ": " + status + ". Original dates, results and certificate are preserved." +
            (request.Action == "move-tab" && request.TargetTab == "current" && status != "Current"
                ? " This record will remain outside the Current tab until its qualification meets the current-skill rules." : "");
    }

    private static bool Matches(SkillRecoveryRow row, string employee, SkillRecoveryTable table, Guid id) =>
        Same(row.EmployeeCode, employee) && row.Table == table && row.RowId == id && ValidVersion(row.Version);
    private static bool ValidVersion(string version)
    {
        try { return Convert.FromBase64String(version).Length == 8; }
        catch (FormatException) { return false; }
    }
    private static bool Contains(string? value, string term) => value?.Contains(term, StringComparison.OrdinalIgnoreCase) == true;
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
