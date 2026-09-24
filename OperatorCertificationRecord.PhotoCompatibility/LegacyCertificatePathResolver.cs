using System;
using System.Text.RegularExpressions;

namespace OperatorCertificationRecord.PhotoCompatibility
{
    /// <summary>Maps known certificate references to the approved legacy share, never arbitrary paths.</summary>
    public static class LegacyCertificatePathResolver
    {
        public const string LegacyRoot = @"\\svr120a\Cert$";
        private static readonly Regex EmployeeCode = new Regex(@"\A[A-Za-z0-9-]{1,32}\z", RegexOptions.CultureInvariant);
        private static readonly Regex DeviceName = new Regex(@"\A(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(?:\.|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly string[] Prefixes =
        {
            "/app/wwwroot/certs/", "/certs/", "/app/wwwroot/uploads/", "/uploads/",
            @"\\svr120a\Cert$\", @"Z:\"
        };

        public static string? Resolve(string? storedPath, string? employeeCode)
        {
            if (string.IsNullOrEmpty(storedPath) || string.IsNullOrEmpty(employeeCode) ||
                !EmployeeCode.IsMatch(employeeCode!) || storedPath!.Length > 240)
                return null;

            string? relative = null;
            foreach (var prefix in Prefixes)
            {
                if (storedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    relative = storedPath.Substring(prefix.Length);
                    break;
                }
            }
            if (relative == null) return null;
            var segments = relative.Replace('\\', '/').Split('/');
            if (segments.Length < 2 || !string.Equals(segments[0], employeeCode, StringComparison.OrdinalIgnoreCase))
                return null;
            foreach (var segment in segments)
            {
                if (!IsSafeSegment(segment)) return null;
            }
            if (!segments[segments.Length - 1].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return null;
            return LegacyRoot + @"\" + string.Join(@"\", segments);
        }

        private static bool IsSafeSegment(string segment)
        {
            if (segment.Length == 0 || segment == "." || segment == ".." ||
                segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal) ||
                DeviceName.IsMatch(segment)) return false;
            foreach (var character in segment)
            {
                if (char.IsControl(character) || "<>:\"|?*%".IndexOf(character) >= 0)
                    return false;
            }
            return true;
        }
    }
}
