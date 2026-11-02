namespace OperatorCertificationRecord.Web.Models;

public class EmployeeQualifiedInput
{
    internal bool IsLegacyFileUpload { get; set; }
    public string? EmpCode { get; set; }
    public string? ProcessName { get; set; }
    public string? OperatorTraining { get; set; }
    public DateTime? TheoryTraining { get; set; }
    public DateTime? OJTTraining { get; set; }
    public DateTime? CertifiedDate { get; set; }
    public string? FullScore { get; set; }
    public string? ActualScore { get; set; }
    public string? TestResult { get; set; }
    public string? JudgmentTheory { get; set; }
    public string? KnowledgeScore { get; set; }
    public string? KnowledgeLevel { get; set; }
    public string? SkillScore { get; set; }
    public string? SkillLevel { get; set; }
    public string? JudgmentPractice { get; set; }
    public DateTime? ExpiryDate { get; set; }
    public string? Verifier { get; set; }
    public string? Remark { get; set; }
    public string? DownloadPath { get; set; }
    public Guid? OcrSkillRowId { get; set; }
    public Guid? CertificateFileId { get; set; }
    public byte[]? FileStorageVersion { get; set; }
}
