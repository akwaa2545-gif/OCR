using System;

namespace OperatorCertificationRecord.Web.Models
{
    public class ExpiringSkill
    {
        public string EmpCode { get; set; } = "";
        public string FirstNameEng { get; set; } = "";
        public string LastNameEng { get; set; } = "";
        public string Process { get; set; } = "";
        public DateTime? ExpiryDate { get; set; }
        public string DeptName { get; set; } = "";
        public string SectName { get; set; } = "";
    }
}
