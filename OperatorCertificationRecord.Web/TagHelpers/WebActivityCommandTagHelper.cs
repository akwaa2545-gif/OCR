using Microsoft.AspNetCore.Razor.TagHelpers;
using OperatorCertificationRecord.Web.Services;

namespace OperatorCertificationRecord.Web.TagHelpers;

[HtmlTargetElement("form")]
public sealed class WebActivityCommandTagHelper(IHttpContextAccessor accessor, WebActivityCommands commands) : TagHelper
{
    public override int Order => 1000;
    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var http = accessor.HttpContext;
        if (http == null || string.IsNullOrWhiteSpace(http.Session.GetString("UserCode")) ||
            !string.Equals(output.Attributes["method"]?.Value?.ToString(), "post", StringComparison.OrdinalIgnoreCase)) return;
        var action = output.Attributes["action"]?.Value?.ToString();
        // Report preparation/template forms have independent submit-handler overrides;
        // command receipts apply only to the instrumented mutation routes above.
        if (!commands.SignsForm(http, action)) return;
        var token = commands.Create(http, action);
        var input = new Microsoft.AspNetCore.Mvc.Rendering.TagBuilder("input");
        input.Attributes["type"] = "hidden";
        input.Attributes["name"] = WebActivityCommands.FieldName;
        input.Attributes["value"] = token;
        output.PostContent.AppendHtml(input);
    }
}
