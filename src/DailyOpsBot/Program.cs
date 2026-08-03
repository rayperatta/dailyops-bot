using DailyOpsBot.Configuration;
using DailyOpsBot.Dashboard;
using DailyOpsBot.Services;
using DailyOpsBot.Services.Delivery;
using DailyOpsBot.Services.Reporting;
using DailyOpsBot.Services.Scheduling;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Polly;
using Polly.Extensions.Http;
using Quartz;
using Serilog;

// Consistent formatting (currency, dates) regardless of the host locale.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture =
    new System.Globalization.CultureInfo("en-US");
System.Globalization.CultureInfo.DefaultThreadCurrentUICulture =
    new System.Globalization.CultureInfo("en-US");

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: new System.Globalization.CultureInfo("en-US"),
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    if (args.Contains("--serve"))
        return await RunDashboardServerAsync(args);

    var builder = Host.CreateApplicationBuilder(args);
    AddSerilog(builder.Services, builder.Configuration);
    AddDailyOpsServices(builder.Services, builder.Configuration);

    using var host = builder.Build();

    if (args.Contains("--generate-data"))
    {
        host.Services.GetRequiredService<SampleDataGenerator>().Generate();
        return 0;
    }

    if (args.Contains("--now"))
    {
        // Run the full pipeline once (analysis + reports + email/demo mode) and exit.
        var report = await host.Services.GetRequiredService<DailyOpsRunner>().RunOnceAsync();
        await host.Services.GetRequiredService<IReportEmailSender>().SendAsync(report);
        host.Services.GetRequiredService<IRunSummaryWriter>().Write(report);
        return 0;
    }

    // Default: run as a long-lived scheduled service.
    var cronExpression = builder.Configuration.GetValue<string>("DailyOps:Scheduler:CronExpression")
                         ?? "0 30 7 * * ?";
    Log.Information("DailyOps Bot started in scheduler mode. Cron: '{Cron}'. " +
                    "Use --now to run once immediately or --serve for the web dashboard. Press Ctrl+C to stop.",
        cronExpression);
    await host.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Log.Fatal(ex, "DailyOps Bot terminated unexpectedly");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Starts the Kestrel-hosted web dashboard (port configurable via DailyOps:Dashboard:Port).</summary>
async Task<int> RunDashboardServerAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);
    AddSerilog(builder.Services, builder.Configuration);
    AddDailyOpsServices(builder.Services, builder.Configuration);

    var port = builder.Configuration.GetValue("DailyOps:Dashboard:Port", 5080);
    builder.WebHost.UseUrls($"http://localhost:{port}");

    var app = builder.Build();

    // Static dashboard assets ship next to the executable (copied to output).
    var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(wwwroot) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(wwwroot) });

    app.MapDashboardEndpoints();

    Log.Information("DailyOps dashboard listening on http://localhost:{Port} " +
                    "(scheduler stays active; Ctrl+C to stop)", port);
    await app.RunAsync();
    return 0;
}

void AddSerilog(IServiceCollection services, IConfiguration configuration)
{
    services.AddSerilog((_, lc) => lc
        .ReadFrom.Configuration(configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: new System.Globalization.CultureInfo("en-US"),
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));
}

void AddDailyOpsServices(IServiceCollection services, IConfiguration configuration)
{
    services.Configure<AppSettings>(configuration.GetSection("DailyOps"));

    // Binance HttpClient with Polly retry + exponential backoff.
    services.AddHttpClient<IBinanceClient, BinanceClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value.Binance;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DailyOpsBot/1.0");
        })
        .AddPolicyHandler(HttpRetryPolicies.GetRetryPolicy());

    services.AddSingleton<ISalesDataLoader, CsvSalesDataLoader>();
    services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
    services.AddSingleton<IReportWriter, ExcelReportWriter>();
    services.AddSingleton<IReportWriter, PdfReportWriter>();
    services.AddSingleton<IReportEmailSender, ReportEmailSender>();
    services.AddSingleton<IRunSummaryWriter, RunSummaryWriter>();
    services.AddTransient<SampleDataGenerator>();
    services.AddTransient<DailyOpsRunner>();

    // Quartz scheduler: one cron trigger (default 07:30 daily, configurable).
    var cron = configuration.GetValue<string>("DailyOps:Scheduler:CronExpression")
               ?? "0 30 7 * * ?";
    services.AddQuartz(q =>
    {
        var jobKey = new JobKey("DailyOpsJob");
        q.AddJob<DailyOpsJob>(opts => opts.WithIdentity(jobKey));
        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("DailyOpsJob-trigger")
            .WithCronSchedule(cron));
    });
    services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);
}

/// <summary>Shared Polly policies for outbound HTTP calls.</summary>
internal static class HttpRetryPolicies
{
    public static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, delay, attempt, _) =>
                    Log.Warning("Binance request failed ({Reason}), retry {Attempt}/3 in {Delay}s",
                        outcome.Exception?.Message ?? outcome.Result.StatusCode.ToString(),
                        attempt, delay.TotalSeconds));
}
