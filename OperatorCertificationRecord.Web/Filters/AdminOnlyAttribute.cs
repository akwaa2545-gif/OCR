using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Filters
{
    public class AdminOnlyAttribute : TypeFilterAttribute
    {
        public AdminOnlyAttribute() : base(typeof(AdminOnlyFilter)) { }
    }
}
