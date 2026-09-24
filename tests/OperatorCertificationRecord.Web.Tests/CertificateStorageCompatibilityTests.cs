using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using OperatorCertificationRecord.Web.Pages;
using OperatorCertificationRecord.Web.Services;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class CertificateStorageCompatibilityTests
{
    [Fact]
    public void ReplacementCertificatesReceiveUniquePortablePdfNames()
    {
        var method = typeof(CertificateStoragePaths).GetMethod("NewFileName");
        Assert.NotNull(method);
        var first = (string)method!.Invoke(null, new object[] { "My training.PDF" })!;
        var second = (string)method.Invoke(null, new object[] { "My training.PDF" })!;
        Assert.Matches("^My_training_[a-f0-9]{32}\\.pdf$", first);
        Assert.NotEqual(first, second);
    }

    [Theory]
    [InlineData(typeof(AddSkillModel))]
    [InlineData(typeof(UpdateSkillModel))]
    [InlineData(typeof(UpdateUserModel))]
    public void EveryWriterUsesConfiguredCertificateRoot(Type page)
    {
        var root = Path.Combine(Path.GetTempPath(), "ocr-certificate-path-test");
        var config = Config(("CertificatePath", root), ("LocalUploadSubfolder", "uploads"));
        Assert.Equal(Path.Combine(root, "102"), Invoke(page, "ResolveUploadDir", config, "102"));
        Assert.Equal(Path.Combine(root, "102", "test.pdf"), Invoke(page, "ResolveDownloadPath", config, "102", "test.pdf"));
    }

    [Theory]
    [InlineData(typeof(AddSkillModel))]
    [InlineData(typeof(UpdateSkillModel))]
    [InlineData(typeof(UpdateUserModel))]
    public void UnconfiguredCertificateRootRetainsLegacyUploadPaths(Type page)
    {
        var config = Config(("LocalUploadSubfolder", "uploads-test"));
        Assert.Equal(Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads-test", "102"), Invoke(page, "ResolveUploadDir", config, "102"));
        Assert.Equal("/uploads-test/102/test.pdf", Invoke(page, "ResolveDownloadPath", config, "102", "test.pdf"));
    }

    [Theory]
    [InlineData("../other")]
    [InlineData("..")]
    [InlineData("102\\other")]
    [InlineData("102:other")]
    [InlineData("102_other")]
    [InlineData("CON")]
    [InlineData("102%20")]
    public void EveryWriterRejectsEmployeePathTraversal(string employee)
    {
        foreach (var page in new[] { typeof(AddSkillModel), typeof(UpdateSkillModel), typeof(UpdateUserModel) })
        {
            var error = Assert.Throws<TargetInvocationException>(() => Invoke(page, "ResolveUploadDir", Config(), employee));
            Assert.IsType<ArgumentException>(error.InnerException);
        }
    }

    [Theory]
    [InlineData("test.html")]
    [InlineData("test.jpg")]
    public void NonPdfCertificateNamesAreRejected(string name)
    {
        var method = typeof(CertificateStoragePaths).GetMethod("NewFileName");
        Assert.NotNull(method);
        var error = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, new object[] { name }));
        Assert.IsType<ArgumentException>(error.InnerException);
    }

    private static IConfiguration Config(params (string Key, string Value)[] values)
    {
        var settings = new Dictionary<string, string?>();
        foreach (var item in values) settings[item.Key] = item.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private static string Invoke(Type page, string method, params object[] args) =>
        (string)page.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, args)!;
}
