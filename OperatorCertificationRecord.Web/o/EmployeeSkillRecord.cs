namespace OperatorCertificationRecord.Web.Models;

public class EmployeeSkillRecord
{
    public string? Process { get; set; }
    public string? CertifyClassification { get; set; }
    public DateTime? Theory { get; set; }
    public DateTime? OJT { get; set; }
    public string? K { get; set; }
    public string? S { get; set; }
    public string? Judgment { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Verifier { get; set; }
    public string? VerifierName { get; set; }
    public string? Remark { get; set; }
    public DateTime? VerifierDate { get; set; }

    // Additional properties for UpdateSkill form
    public string? OperatorTraining { get; set; }
    public DateTime? TheoryTraining { get; set; }
    public DateTime? OJTTraining { get; set; }
    public string? FullScore { get; set; }
    public string? ActualScore { get; set; }
    public string? TestResult { get; set; }
    public string? JudgmentTheory { get; set; }
    public string? KnowledgeScore { get; set; }
    public string? KnowledgeLevel { get; set; }
    public string? SkillScore { get; set; }
    public string? SkillLevel { get; set; }
    public string? JudgmentPractice { get; set; }
    public string? DownloadPath { get; set; }
}

public class EmployeeDisqualifiedRecord
{
    public string? Process { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public string? K { get; set; }
    public string? S { get; set; }
    public string? Verifier { get; set; }
    public string? VerifierName { get; set; }
    public DateTime? DisqualifiedDate { get; set; }
    public string? DisqualifiedBy { get; set; }
    public string? TheReason { get; set; }
    public string? Remark { get; set; }
}

public class EmployeeObsoletedRecord
{
    public string? Process { get; set; }
    public string? CertifyClassification { get; set; }
    public DateTime? Theory { get; set; }
    public DateTime? OJT { get; set; }
    public string? Result { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public string? K { get; set; }
    public string? S { get; set; }
    public string? Judgment { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Verifier { get; set; }
    public string? VerifierName { get; set; }
    public string? Remark { get; set; }
}
