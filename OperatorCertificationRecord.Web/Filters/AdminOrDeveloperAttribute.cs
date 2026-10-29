using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class AdminOrDeveloperAttribute : TypeFilterAttribute
{
    public AdminOrDeveloperAttribute() : base(typeof(AdminOrDeveloperFilter)) { }
}
