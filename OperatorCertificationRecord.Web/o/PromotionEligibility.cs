namespace OperatorCertificationRecord.Web.Models;

public class PromotionEligibility
{
    public bool IsEligible { get; set; }
    public string CurrentGrade { get; set; } = string.Empty;
    public string NextGrade { get; set; } = string.Empty;
    public int QualifiedCount { get; set; }
    public int RequiredQualified { get; set; }
    public int TenureMonths { get; set; }
    public int RequiredTenureMonths { get; set; }
    public List<string> Reasons { get; set; } = new();
    public bool CanPromote => IsEligible && !string.IsNullOrEmpty(NextGrade);
    // True when external state (eg. resignation) prevents promotion even if eligible
    public bool IsBlockedDueToResign { get; set; } = false;
}

public static class PromotionRules
{
    // Promotion path: All grades below 54G → 54G (Line Leader I) - Maximum promotable grade
    private static readonly Dictionary<string, string> GradeProgression = new()
    {
        { "51T", "54T" },  // Operator I → Line Leader I
        { "52T", "54T" },  // Operator II → Line Leader I
        { "53T", "54T" },  // Sr.Operator → Line Leader I
        { "54T", "" },     // Line Leader I (Maximum grade - no further promotion)
        { "55T", "" },     // Line Leader II (Already above promotion cap)
        { "56T", "" },     // Sr.Line Leader (Already above promotion cap)
        { "57T", "" },     // Chief Mfg. (Already above promotion cap)
        { "58T", "" },     // Sr.Chief Mfg. (Already above promotion cap)
        { "59T", "" },     // Mfg. Supervisor (Already above promotion cap)
        { "60T", "" },     // Sr.Mfg. Supervisor (Already above promotion cap)
        { "61T", "" },     // Technical Manager I (Already above promotion cap)
        { "62T", "" }      // Technical Manager II (Already above promotion cap)
    };

    // Requirements: [JobGrade] = (MinQualified, MinTenureMonths)
    private static readonly Dictionary<string, (int MinQualified, int MinMonths)> Requirements = new()
    {
        { "51T", (1, 0) },    // Operator I → Line Leader I
        { "52T", (1, 0) },    // Operator II → Line Leader I
        { "53T", (1, 0) },    // Sr.Operator → Line Leader I
        { "54T", (999, 999) }, // Line Leader I (Maximum - cannot promote)
        { "55T", (999, 999) }, // Line Leader II (Above cap)
        { "56T", (999, 999) }, // Sr.Line Leader (Above cap)
        { "57T", (999, 999) }, // Chief Mfg. (Above cap)
        { "58T", (999, 999) }, // Sr.Chief Mfg. (Above cap)
        { "59T", (999, 999) }, // Mfg. Supervisor (Above cap)
        { "60T", (999, 999) }, // Sr.Mfg. Supervisor (Above cap)
        { "61T", (999, 999) }, // Technical Manager I (Above cap)
        { "62T", (999, 999) }  // Technical Manager II (Above cap)
    };

    public static PromotionEligibility CheckEligibility(
        string currentGrade,
        int qualifiedCount,
        DateTime joinDate)
    {
        var result = new PromotionEligibility
        {
            CurrentGrade = currentGrade,
            QualifiedCount = qualifiedCount,
            TenureMonths = GetTenureMonths(joinDate)
        };

        // Check if grade exists in progression
        if (!GradeProgression.ContainsKey(currentGrade))
        {
            result.Reasons.Add($"Invalid job grade: {currentGrade}");
            return result;
        }

        result.NextGrade = GradeProgression[currentGrade];

        // Already at maximum grade or above promotion cap
        if (string.IsNullOrEmpty(result.NextGrade))
        {
            if (currentGrade == "54G")
            {
                result.Reasons.Add("Already at maximum promotable grade (54G - Line Leader I)");
            }
            else
            {
                result.Reasons.Add($"Grade {currentGrade} is above the promotion cap (only grades 51G-53G can be promoted to 54G)");
            }
            return result;
        }

        // Get requirements for current grade
        if (!Requirements.ContainsKey(currentGrade))
        {
            result.Reasons.Add("No promotion rules defined for this grade");
            return result;
        }

        var (minQualified, minMonths) = Requirements[currentGrade];
        result.RequiredQualified = minQualified;
        result.RequiredTenureMonths = minMonths;

        // Check qualifications
        bool hasEnoughQualifications = qualifiedCount >= minQualified;
        bool hasEnoughTenure = result.TenureMonths >= minMonths;

        if (!hasEnoughQualifications)
        {
            result.Reasons.Add($"Needs {minQualified} qualified processes (currently has {qualifiedCount})");
        }

        if (!hasEnoughTenure)
        {
            var remainingMonths = minMonths - result.TenureMonths;
            result.Reasons.Add($"Needs {minMonths} months tenure (currently has {result.TenureMonths} months, {remainingMonths} more needed)");
        }

        result.IsEligible = hasEnoughQualifications && hasEnoughTenure;

        if (result.IsEligible)
        {
            result.Reasons.Add($"✓ Eligible for promotion from {currentGrade} to {result.NextGrade}");
        }
        else
        {
            result.Reasons.Add($"✗ Not eligible for promotion from {currentGrade} to {result.NextGrade}");
        }

        return result;
    }

    private static int GetTenureMonths(DateTime joinDate)
    {
        var today = DateTime.Today;
        int months = ((today.Year - joinDate.Year) * 12) + today.Month - joinDate.Month;
        if (today.Day < joinDate.Day)
            months--;
        return Math.Max(0, months);
    }

    public static string GetNextGrade(string currentGrade)
    {
        return GradeProgression.ContainsKey(currentGrade) 
            ? GradeProgression[currentGrade] 
            : string.Empty;
    }

 
    
}
