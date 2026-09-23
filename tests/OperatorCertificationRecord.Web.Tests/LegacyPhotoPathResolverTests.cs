using System.IO;
using OperatorCertificationRecord.PhotoCompatibility;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public class LegacyPhotoPathResolverTests
{
    private const string LegacyRoot = @"\\svr120a\PhotoEmp$";

    [Fact]
    public void Resolve_ExistingUncPath_IsUnchanged()
    {
        const string storedPath = @"\\svr120a\PhotoEmp$\2609509.jpg";

        Assert.Equal(storedPath, LegacyPhotoPathResolver.Resolve(storedPath, "2609509", LegacyRoot));
    }

    [Theory]
    [InlineData("/app/photos/2603577_639133057204214606.jpg", "jpg")]
    [InlineData("/uploads/2603577_639133057204214606.PNG", "png")]
    [InlineData("/photos/anything.jpeg", "jpeg")]
    [InlineData("/app/wwwroot/photos/anything.JPG", "jpg")]
    public void Resolve_WebPath_UsesRowEmployeeCodeAndExtension(string storedPath, string extension)
    {
        var expected = Path.Combine(LegacyRoot, $"2603577.{extension}");

        Assert.Equal(expected, LegacyPhotoPathResolver.Resolve(storedPath, "2603577", LegacyRoot));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankPath_ReturnsNull(string? storedPath)
    {
        Assert.Null(LegacyPhotoPathResolver.Resolve(storedPath, "2603577", LegacyRoot));
    }

    [Theory]
    [InlineData("/app/photos/no-extension")]
    [InlineData("/app/photos/photo.gif")]
    [InlineData("relative/photo.jpg")]
    [InlineData("C:\\photos\\2603577.jpg")]
    public void Resolve_UnrecognizedOrUnsafePath_IsUnchanged(string storedPath)
    {
        Assert.Equal(storedPath, LegacyPhotoPathResolver.Resolve(storedPath, "2603577", LegacyRoot));
    }

    [Fact]
    public void Resolve_UnsafeEmployeeCode_IsUnchanged()
    {
        const string storedPath = "/app/photos/2603577_123.jpg";

        Assert.Equal(storedPath, LegacyPhotoPathResolver.Resolve(storedPath, "../2603577", LegacyRoot));
    }
}
