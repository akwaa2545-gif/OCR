using System;
using System.IO;
using System.Text.RegularExpressions;

namespace OperatorCertificationRecord.PhotoCompatibility
{
    public static class LegacyPhotoPathResolver
    {
        private static readonly Regex EmployeeCodePattern =
            new Regex("^[A-Za-z0-9-]{1,32}$", RegexOptions.CultureInvariant);

        private static readonly string[] WebPathPrefixes =
        {
            "/app/photos/",
            "/app/wwwroot/photos/",
            "/app/wwwroot/uploads/",
            "/photos/",
            "/uploads/"
        };

        public static string? Resolve(string? storedPath, string? employeeCode, string legacyRoot)
        {
            if (string.IsNullOrWhiteSpace(storedPath))
            {
                return null;
            }

            if (storedPath!.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return storedPath;
            }

            var normalizedPath = storedPath!.Replace('\\', '/');
            if (!IsWebPhotoPath(normalizedPath) ||
                string.IsNullOrWhiteSpace(employeeCode) ||
                !EmployeeCodePattern.IsMatch(employeeCode!) ||
                string.IsNullOrWhiteSpace(legacyRoot))
            {
                return storedPath;
            }

            var extension = Path.GetExtension(normalizedPath).ToLowerInvariant();
            if (extension != ".jpg" && extension != ".jpeg" && extension != ".png")
            {
                return storedPath;
            }

            return Path.Combine(legacyRoot, employeeCode + extension);
        }

        private static bool IsWebPhotoPath(string path)
        {
            foreach (var prefix in WebPathPrefixes)
            {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
