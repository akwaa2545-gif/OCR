using OperatorCertificationRecord.PhotoCompatibility;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public class LegacyCertificatePathResolverTests
{
    [Theory]
    [InlineData("/app/wwwroot/certs/102/course.pdf")]
    [InlineData("/certs/102/course.pdf")]
    [InlineData("/app/wwwroot/uploads/102/course.pdf")]
    [InlineData("/uploads/102/course.pdf")]
    [InlineData(@"\\svr120a\Cert$\102\course.pdf")]
    [InlineData(@"Z:\102\course.pdf")]
    public void Resolve_ApprovedPaths_PreservesEmployeeFolder(string path)
    {
        Assert.Equal(@"\\svr120a\Cert$\102\course.pdf", LegacyCertificatePathResolver.Resolve(path, "102"));
    }

    [Fact]
    public void Resolve_NestedUnicodeName_PreservesSafeSegments()
    {
        Assert.Equal(@"\\svr120a\Cert$\102\2026\การฝึก A.PDF",
            LegacyCertificatePathResolver.Resolve("/certs/102/2026/การฝึก A.PDF", "102"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/certs/103/course.pdf")]
    [InlineData("/certs/course.pdf")]
    [InlineData("/certs/102/../103/course.pdf")]
    [InlineData("/certs/102/%2e%2e/course.pdf")]
    [InlineData("/certs/102//course.pdf")]
    [InlineData("/certs/102/course.pdf:evil")]
    [InlineData("/certs/102/course.exe")]
    [InlineData("/certs/102/NUL.pdf")]
    [InlineData("/certs/102/COM1.pdf")]
    [InlineData("/certs/102/course .pdf ")]
    [InlineData("/certs/102/course?.pdf")]
    [InlineData("/certs/102/a\u0000.pdf")]
    [InlineData(@"\\other\Cert$\102\course.pdf")]
    [InlineData(@"\\svr120a\Cert$other\102\course.pdf")]
    [InlineData(@"\\?\UNC\svr120a\Cert$\102\course.pdf")]
    [InlineData(@"C:\102\course.pdf")]
    [InlineData("/app/wwwroot/certs-other/102/course.pdf")]
    public void Resolve_UnsafeOrUnrecognizedPath_Rejects(string? path)
    {
        Assert.Null(LegacyCertificatePathResolver.Resolve(path, "102"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../102")]
    public void Resolve_InvalidEmployee_Rejects(string? employee)
    {
        Assert.Null(LegacyCertificatePathResolver.Resolve("/certs/102/course.pdf", employee));
    }
}
