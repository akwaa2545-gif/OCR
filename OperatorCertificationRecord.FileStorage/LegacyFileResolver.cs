using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OperatorCertificationRecord.FileStorage
{
    public sealed class LegacyFileResolver
    {
        private readonly string[] roots;

        public LegacyFileResolver(IEnumerable<string> approvedRoots)
        {
            if (approvedRoots == null)
                throw new ArgumentNullException(nameof(approvedRoots));
            roots = approvedRoots
                .Select(path =>
                    Path.GetFullPath(path)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar
                )
                .ToArray();
            if (roots.Length == 0)
                throw new ArgumentException("At least one approved root is required.");
        }

        public string? Resolve(FileOwnerState owner)
        {
            if (owner.FileId.HasValue)
                throw new InvalidOperationException(
                    "A SQL-authoritative attachment must be read from SQL."
                );
            return string.IsNullOrWhiteSpace(owner.LegacyPath) ? null : Resolve(owner.LegacyPath!);
        }

        public string? Resolve(string relativeKey)
        {
            SqlEmployeeFileRepository.CanonicalizeAlias(relativeKey);
            var key = relativeKey.Replace('\\', '/').Trim().TrimStart('/');
            foreach (var root in roots)
            {
                var candidate = Path.GetFullPath(
                    Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar))
                );
                if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Attachment path is outside the approved root.");
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }
    }
}
