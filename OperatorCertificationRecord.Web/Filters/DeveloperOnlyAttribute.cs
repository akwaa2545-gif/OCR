using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class DeveloperOnlyAttribute : TypeFilterAttribute
{
    public DeveloperOnlyAttribute() : base(typeof(DeveloperOnlyFilter)) { }
}
