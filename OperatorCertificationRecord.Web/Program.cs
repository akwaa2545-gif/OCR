using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.ResponseCompression;
using OperatorCertificationRecord.Web.Services;
using OperatorCertificationRecord.Web.Services.Logging;
using QuestPDF.Infrastructure;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

// Configure Serilog before anything else
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithEnvironmentName()
    .Enrich.WithThreadId()
    .Enrich.WithProperty("Application", "OperatorCertificationRecord")
    // Console: Simple, clean format for easy reading
    .WriteTo.Console(
        outputTemplate: "{Timestamp:HH:mm:ss} {Level:u3} | {Message:lj}{NewLine}{Exception}")
    // File: Detailed log with all properties (for debugging)
    .WriteTo.File(
        path: "logs/app-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{RequestId}] {Message:lj}{NewLine}{Exception}")
    // JSON File: Structured logs for log analysis tools
    .WriteTo.File(
        new CompactJsonFormatter(),
        path: "logs/app-.json",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14)
    .CreateLogger();

try
{
    Log.Information("Starting OperatorCertificationRecord Web Application...");

    var builder = WebApplication.CreateBuilder(args);
    
    // Use Serilog as the logging provider
    builder.Host.UseSerilog();

    // Configure persistent data protection keys (survives container restarts)
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo("/app/dataprotection"))
        .SetApplicationName("OperatorCertificationRecord");

    // Add services to the container
    builder.Services.AddRazorPages();
    builder.Services.AddControllers();
    builder.Services.AddSession(options =>
    {
        options.IdleTimeout = TimeSpan.FromMinutes(30);
        options.Cookie.HttpOnly = true;
        options.Cookie.IsEssential = true;
    });

    // Add Logging Service (singleton for in-memory log storage)
    builder.Services.AddSingleton<LoggingService>();

    // Admin service to track administrators (from appsettings or App_Data/admins.json)
    builder.Services.AddSingleton<AdminService>();
    builder.Services.AddScoped<OperatorCertificationRecord.Web.Filters.AdminOnlyFilter>();

    // Add database services
    builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
    builder.Services.AddScoped<EmployeeService>();
    builder.Services.AddScoped<DepartmentService>();
    builder.Services.AddScoped<SectionService>();
    builder.Services.AddScoped<WorkshopService>();
    builder.Services.AddScoped<JobGradeService>();
    builder.Services.AddScoped<OperatorTrainingService>();
    builder.Services.AddScoped<PdfExportService>();
    builder.Services.AddScoped<ReportBuilderService>();
    builder.Services.AddScoped<ReportAutomationService>();
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IDashboardCacheService, DashboardCacheService>();
    builder.Services.AddHostedService<DashboardCacheRefreshService>();

    // Response compression (gzip + brotli) — disabled in Development so dotnet-watch can inject the refresh script
    if (!builder.Environment.IsDevelopment())
    {
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                new[] { "application/json", "image/svg+xml" });
        });
    }

    var app = builder.Build();

    // ---- Pre-warm dashboard cache BEFORE server starts accepting requests ----
    Log.Information("Pre-warming dashboard cache (blocking startup)...");
    try
    {
        var cache = app.Services.GetRequiredService<IDashboardCacheService>();
        await cache.GetDashboardDataAsync();
        Log.Information("Dashboard cache ready — server will now accept requests.");
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Dashboard cache warmup failed — first request will be slower.");
    }

    // Configure QuestPDF license (Community is free for small teams)
    QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

    // Configure the HTTP request pipeline
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        app.UseHsts();
        app.UseHttpsRedirection();
    }

    // Enable response compression in non-development environments only (keeps dotnet-watch script injection working in Development)
    if (!app.Environment.IsDevelopment())
    {
        // Compression must come before static files so HTML gets compressed too
        app.UseResponseCompression();
    }

    // Static files with long browser cache (1 year) — CSS/JS/images/fonts
    app.UseStaticFiles(new StaticFileOptions
    {
        OnPrepareResponse = ctx =>
        {
            ctx.Context.Response.Headers.Append(
                "Cache-Control", "public,max-age=31536000,immutable");
        }
    });

    app.UseRouting();
    app.UseSession();
    
    // Add custom request logging middleware (after session, so we can access user info)
    app.UseRequestLogging();

    app.MapGet("/", context => 
    {
        context.Response.Redirect("/Dashboard");
        return Task.CompletedTask;
    });

    app.MapRazorPages();
    app.MapControllers();

    // Minimal API endpoint to return sections for a given department (used by client-side JS)
    app.MapGet("/api/sections", async (string deptId, OperatorCertificationRecord.Web.Services.SectionService sectionService) =>
    {
        if (string.IsNullOrWhiteSpace(deptId))
            return Results.Json(new List<object>());
        var sections = await sectionService.GetSectionsByDepartmentAsync(deptId);
        return Results.Json(sections);
    });

    app.MapGet("/api/processes", async (string workshopId, OperatorCertificationRecord.Web.Services.WorkshopService workshopService) =>
    {
        if (string.IsNullOrWhiteSpace(workshopId))
            return Results.Json(new List<object>());
        var processes = await workshopService.GetProcessesByWorkshopAsync(workshopId);
        return Results.Json(processes);
    });

    app.MapGet("/api/operatortrainings", async (OperatorCertificationRecord.Web.Services.OperatorTrainingService otService) =>
    {
        var list = await otService.GetAllOperatorTrainingAsync();
        return Results.Json(list);
    });

    Log.Information("Application started successfully. Listening on configured ports.");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly!");
}
finally
{
    Log.Information("Application shutting down...");
    Log.CloseAndFlush();
}

