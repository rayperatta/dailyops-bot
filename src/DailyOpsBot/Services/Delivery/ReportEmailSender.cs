using System.Text;
using DailyOpsBot.Configuration;
using DailyOpsBot.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace DailyOpsBot.Services.Delivery;

public interface IReportEmailSender
{
    /// <summary>
    /// Sends the report by email. When no SMTP credentials are configured the sender
    /// runs in <b>demo mode</b>: it logs the full email body and attachment paths instead.
    /// </summary>
    Task SendAsync(DailyReport report, CancellationToken cancellationToken = default);
}

/// <summary>Delivers the daily report via SMTP using MailKit.</summary>
public sealed class ReportEmailSender(
    IOptions<AppSettings> settings,
    ILogger<ReportEmailSender> logger) : IReportEmailSender
{
    public async Task SendAsync(DailyReport report, CancellationToken cancellationToken = default)
    {
        var email = settings.Value.Email;
        var message = BuildMessage(report, email);

        if (!email.IsConfigured)
        {
            report.EmailStatus = "demo";
            LogDemoMode(report, message);
            return;
        }

        using var client = new SmtpClient();
        var secure = email.EnableSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;
        await client.ConnectAsync(email.Host, email.Port, secure, cancellationToken);
        await client.AuthenticateAsync(email.User, email.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        report.EmailStatus = "sent";

        logger.LogInformation("Report emailed to {To} via {Host}:{Port}", email.To, email.Host, email.Port);
    }

    private static MimeMessage BuildMessage(DailyReport report, EmailOptions email)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(email.From));
        if (!string.IsNullOrWhiteSpace(email.To))
            message.To.Add(MailboxAddress.Parse(email.To));

        var status = report.HasAnomalies
            ? $"{report.Anomalies.Count} anomaly(ies) detected"
            : "all clear";
        message.Subject = $"DailyOps Report {report.GeneratedAtUtc:yyyy-MM-dd} — {status}";

        var body = new BodyBuilder { TextBody = BuildBodyText(report) };
        foreach (var file in report.OutputFiles.Where(File.Exists))
            body.Attachments.Add(file);
        message.Body = body.ToMessageBody();
        return message;
    }

    internal static string BuildBodyText(DailyReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DailyOps report generated at {report.GeneratedAtUtc:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine();
        sb.AppendLine("KEY METRICS");
        sb.AppendLine($"  Sales rows:      {report.Sales.TotalRows:N0} ({report.Sales.FileCount} files)");
        sb.AppendLine($"  Total revenue:   {report.Sales.TotalRevenue:C2}");
        sb.AppendLine($"  Units sold:      {report.Sales.TotalUnits:N0}");
        sb.AppendLine($"  Duplicate rows:  {report.Sales.DuplicateRowCount}");
        sb.AppendLine($"  Tickers tracked: {report.TopTickers.Count}");
        sb.AppendLine();
        sb.AppendLine(report.HasAnomalies ? "ANOMALIES" : "ANOMALIES: none — all clear");
        foreach (var a in report.Anomalies)
            sb.AppendLine($"  [{a.Severity}] {a.Type}: {a.Description}");
        sb.AppendLine();
        sb.AppendLine("Full details in the attached Excel workbook and PDF summary.");
        return sb.ToString();
    }

    private void LogDemoMode(DailyReport report, MimeMessage message)
    {
        logger.LogWarning("SMTP not configured — DEMO MODE: email NOT sent. " +
                          "Set DailyOps:Email settings (or appsettings.Local.json) to enable delivery.");
        logger.LogInformation("--- Demo email --------------------------------------------------");
        logger.LogInformation("To: {To}", settings.Value.Email.To is { Length: > 0 } to ? to : "(not configured)");
        logger.LogInformation("Subject: {Subject}", message.Subject);
        foreach (var line in BuildBodyText(report).Split('\n'))
            logger.LogInformation("{Line}", line.TrimEnd('\r'));
        logger.LogInformation("Attachments: {Files}",
            report.OutputFiles.Count > 0 ? string.Join(", ", report.OutputFiles) : "(none)");
        logger.LogInformation("-----------------------------------------------------------------");
    }
}
