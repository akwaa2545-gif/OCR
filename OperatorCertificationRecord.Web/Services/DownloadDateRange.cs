using System.Data.SqlTypes;

namespace OperatorCertificationRecord.Web.Services;

/// <summary>Validates dates before SQL binding and exclusive end-of-day conversion.</summary>
public static class DownloadDateRange
{
    public static bool TryValidate(DateTime? start, DateTime? end, bool required, out string error)
    {
        error = "";
        if (!start.HasValue && !end.HasValue && !required) return true;
        if (!start.HasValue || !end.HasValue)
            error = "Please provide both start and end dates in yyyy-MM-dd format.";
        else if (start.Value.Date > end.Value.Date)
            error = "The end date must be on or after the start date.";
        else if (start.Value.Date < SqlDateTime.MinValue.Value.Date || end.Value.Date >= DateTime.MaxValue.Date)
            error = "Report dates must be between 1753-01-01 and 9999-12-30.";
        return error.Length == 0;
    }
}
