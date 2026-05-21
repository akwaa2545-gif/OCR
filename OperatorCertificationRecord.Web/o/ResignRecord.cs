namespace OperatorCertificationRecord.Web.Models
{
    public class ResignRecord
    {
        public string EmpCode { get; set; } = "";
        public string FirstNameEng { get; set; } = "";
        public string LastNameEng { get; set; } = "";
        public string DeptName { get; set; } = "";
        public string SectName { get; set; } = "";
        public string Process { get; set; } = "";
        public DateTime? ResignDate { get; set; }
        public string ResignBy { get; set; } = "";
        public string Remark { get; set; } = "";
    }
}
