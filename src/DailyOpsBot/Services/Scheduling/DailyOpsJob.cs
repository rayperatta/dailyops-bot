using DailyOpsBot.Models;
using DailyOpsBot.Services.Delivery;
using Microsoft.Extensions.Logging;
using Quartz;

namespace DailyOpsBot.Services.Scheduling;

/// <summary>Quartz job that runs the full pipeline and emails the result.</summary>
[DisallowConcurrentExecution]
public sealed class DailyOpsJob(
    DailyOpsRunner runner,
    IReportEmailSender emailSender,
    IRunSummaryWriter runSummaryWriter,
    ILogger<DailyOpsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Scheduled DailyOps job triggered at {Time:u}", DateTime.UtcNow);
        DailyReport report = await runner.RunOnceAsync(context.CancellationToken);
        await emailSender.SendAsync(report, context.CancellationToken);
        runSummaryWriter.Write(report);
    }
}
