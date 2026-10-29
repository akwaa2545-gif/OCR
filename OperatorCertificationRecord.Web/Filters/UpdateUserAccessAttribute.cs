using Microsoft.AspNetCore.Mvc;

namespace OperatorCertificationRecord.Web.Filters;

public sealed class UpdateUserAccessAttribute : TypeFilterAttribute
{
    public UpdateUserAccessAttribute() : base(typeof(UpdateUserAccessFilter)) { }
}
