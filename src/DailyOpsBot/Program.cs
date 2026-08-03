using DailyOpsBot.Configuration;
using DailyOpsBot.Services;
using DailyOpsBot.Services.Delivery;
using DailyOpsBot.Services.Reporting;
using DailyOpsBot.Services.Scheduling;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    var builder = Host.CreateApplicationBuilder(args);

    builder.Services.AddSerilog((_, lc) => lc
        .ReadFrom.Configuration(builder.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console(formatProvider: new System.Globalization.CultureInfo("en-US"),
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

    builder.Services.Configure<AppSettings>(builder.Configuration.GetSection("DailyOps"));

    // Binance HttpClient with Polly retry + exponential backoff.
    builder.Services.AddHttpClient<IBinanceClient, BinanceClient>((sp, client) =>
        {
            var settings = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AppSettings>>().Value.Binance;
            client.BaseAddress = new Uri(settings.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(settings.RequestTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DailyOpsBot/1.0");
        })
        .AddPolicyHandler(HttpRetryPolicies.GetRetryPolicy());

    builder.Services.AddSingleton<ISalesDataLoader, CsvSalesDataLoader>();
    builder.Services.AddSingleton<IAnomalyDetector, AnomalyDetector>();
    builder.Services.AddSingleton<IReportWriter, ExcelReportWriter>();
    builder.Services.AddSingleton<IReportWriter, PdfReportWriter>();
    builder.Services.AddSingleton<IReportEmailSender, ReportEmailSender>();
    builder.Services.AddTransient<SampleDataGenerator>();
    builder.Services.AddTransient<DailyOpsRunner>();

    // Quartz scheduler: one cron trigger (default 07:30 daily, configurable).
    var cron = builder.Configuration.GetValue<string>("DailyOps:Scheduler:CronExpression")
               ?? "0 30 7 * * ?";
    builder.Services.AddQuartz(q =>
    {
        var jobKey = new JobKey("DailyOpsJob");
        q.AddJob<DailyOpsJob>(opts => opts.WithIdentity(jobKey));
        q.AddTrigger(opts => opts
            .ForJob(jobKey)
            .WithIdentity("DailyOpsJob-trigger")
            .WithCronSchedule(cron));
    });
    builder.Services.AddQuartzHostedService(opts => opts.WaitForJobsToComplete = true);

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
        return 0;
    }

    // Default: run as a long-lived scheduled service.
    Log.Information("DailyOps Bot started in scheduler mode. Cron: '{Cron}'. " +
                    "Use --now to run once immediately. Press Ctrl+C to stop.", cron);
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
