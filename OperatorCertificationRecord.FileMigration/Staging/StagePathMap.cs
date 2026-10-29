namespace OperatorCertificationRecord.FileMigration.Staging;

public sealed record StagePathMap(string Kind, string LogicalPrefix, string PhysicalRoot)
{
    public string? Resolve(string reference)
    {
        if (Kind is not ("Photo" or "Certificate")) throw new InvalidDataException("Unknown staging map kind.");
        var path = reference.Replace('\\', '/'); var prefix = LogicalPrefix.Replace('\\', '/');
        var absolute = path.StartsWith('/') || path.Length > 1 && path[1] == ':';
        string relative;
        if (prefix.Length == 0) { if (absolute) return null; relative = path; }
        else
        {
            prefix = prefix.TrimEnd('/') + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            relative = path[prefix.Length..];
        }
        _ = SourceFiles.CanonicalKey(relative);
        var result = Path.GetFullPath(Path.Combine(PhysicalRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        SourceFiles.AssertUnderRoots(result, new[] { PhysicalRoot });
        return result;
    }

    public static StagePathMap[] Parse(string input) => input.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(value =>
    {
        var parts = value.Split('|');
        if (parts.Length != 3 || parts[0] is not ("Photo" or "Certificate") || string.IsNullOrWhiteSpace(parts[2]))
            throw new InvalidOperationException("Path maps use Kind|logical-prefix|physical-root separated by semicolons.");
        return new StagePathMap(parts[0], parts[1], Path.GetFullPath(parts[2]));
    }).ToArray();
}
