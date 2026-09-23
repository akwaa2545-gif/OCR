using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using OperatorCertificationRecord.Web.Controllers;
using OperatorCertificationRecord.Web.Tests.Fakes;
using Xunit;

namespace OperatorCertificationRecord.Web.Tests;

public sealed class PhotoControllerTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "ocr-photo-controller-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void GetPhoto_ConfiguredUploadRoot_ReturnsCanonicalPng()
    {
        var uploadRoot = Path.Combine(_testRoot, "uploads");
        Directory.CreateDirectory(uploadRoot);
        var expected = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
        File.WriteAllBytes(Path.Combine(uploadRoot, "2603577_123.png"), expected);

        var result = CreateController(uploadRoot, Path.Combine(_testRoot, "legacy"))
            .GetPhoto("2603577_123.png");

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        Assert.Equal(expected, ReadAllBytes(file.FileStream));
    }

    [Fact]
    public void GetPhoto_CanonicalFileMissing_ReturnsStableLegacyMirror()
    {
        var legacyRoot = Path.Combine(_testRoot, "legacy");
        Directory.CreateDirectory(legacyRoot);
        var expected = new byte[] { 0xFF, 0xD8, 0xFF };
        File.WriteAllBytes(Path.Combine(legacyRoot, "2603577.jpg"), expected);

        var result = CreateController(Path.Combine(_testRoot, "uploads"), legacyRoot)
            .GetPhoto("2603577_123.jpg");

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(expected, ReadAllBytes(file.FileStream));
    }

    [Fact]
    public void GetPhoto_WithoutAuthenticatedSession_IsUnauthorized()
    {
        var result = CreateController(
                Path.Combine(_testRoot, "uploads"),
                Path.Combine(_testRoot, "legacy"),
                authenticated: false)
            .GetPhoto("2603577_123.jpg");

        Assert.IsType<UnauthorizedResult>(result);
    }

    private PhotoController CreateController(string uploadRoot, string legacyRoot, bool authenticated = true)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PhotoStorage:UploadRoot"] = uploadRoot,
                ["PhotoStorage:LegacyMirrorRoot"] = legacyRoot
            })
            .Build();
        var webRoot = Path.Combine(_testRoot, "wwwroot");
        Directory.CreateDirectory(webRoot);

        var controller = new PhotoController(
            configuration,
            NullLogger<PhotoController>.Instance,
            new TestWebHostEnvironment(webRoot));
        var httpContext = new DefaultHttpContext();
        var session = new TestSession();
        if (authenticated)
        {
            session.SetString("UserCode", "2603577");
        }
        httpContext.Features.Set<ISessionFeature>(new TestSessionFeature { Session = session });
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using (stream)
        using (var memory = new MemoryStream())
        {
            stream.CopyTo(memory);
            return memory.ToArray();
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment(string webRootPath) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = webRootPath;
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = webRootPath;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestSessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }
}
