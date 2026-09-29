using MailKit.Net.Smtp;
using Microsoft.EntityFrameworkCore;
using MimeKit;
namespace HealthChecker;

public interface IResultMailer { Task Send(SiteMonitor monitor, CheckResult result, CancellationToken ct); }
public class ResultMailer(IConfiguration config) : IResultMailer
{
    public async Task Send(SiteMonitor monitor, CheckResult result, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Website Health Checker", "health@showcase.test"));
        message.To.Add(MailboxAddress.Parse(monitor.Email));
        message.Subject = $"Website check: {(result.Healthy ? "Healthy" : "Unhealthy")} [{result.Id}]";
        message.Body = new TextPart("plain") { Text = $"Website: {monitor.Url}\nStatus: {(result.Healthy ? "Healthy" : "Unhealthy")}\nHTTP: {result.StatusCode}\nResponse: {result.ResponseTimeMs} ms\nTime: {result.CheckedAt:O}\nError: {result.Error ?? "None"}\nCheck ID: {result.Id}" };
        using var smtp = new SmtpClient { Timeout = 10000 };
        await smtp.ConnectAsync(config["Smtp:Host"] ?? "mailpit", config.GetValue<int?>("Smtp:Port") ?? 1025, MailKit.Security.SecureSocketOptions.None, deadline.Token);
        await smtp.SendAsync(message, deadline.Token);
        await smtp.DisconnectAsync(true, deadline.Token);
    }
}
// Bounded striped locks coordinate checks, pause and deletion in this single-instance demo.
public class MonitorLocks
{
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 256).Select(_ => new SemaphoreSlim(1)).ToArray();
    public SemaphoreSlim For(Guid id) => gates[(uint)id.GetHashCode() % (uint)gates.Length];
}
public class CheckService(MonitorDb db, IWebsiteChecker checker, IResultMailer mailer, MonitorLocks locks, ILogger<CheckService> log)
{
    public static DateTimeOffset NextDue(DateTimeOffset completedAt) => completedAt.AddMinutes(5);
    public async Task<CheckResult?> Run(Guid id, bool scheduled, CancellationToken ct, bool waitForLock = false)
    {
        var gate = locks.For(id);
        if (waitForLock) await gate.WaitAsync(ct);
        else if (!await gate.WaitAsync(0, ct)) throw new CheckBusyException();
        try
        {
            var monitor = await db.Monitors.FindAsync([id], ct);
            if (monitor is null || (scheduled && (monitor.Paused || monitor.NextCheckAt > DateTimeOffset.UtcNow))) return null;
            var result = await checker.Check(monitor.Url, ct);
            result.MonitorId = id;
            db.Checks.Add(result);
            monitor.NextCheckAt = NextDue(DateTimeOffset.UtcNow);
            await db.SaveChangesAsync(ct);
            try { await mailer.Send(monitor, result, ct); result.EmailStatus = "Sent"; }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                result.EmailStatus = "Failed";
                log.LogWarning("Email delivery failed for check {CheckId}", result.Id);
            }
            await db.SaveChangesAsync(ct);
            return result;
        }
        finally { gate.Release(); }
    }
}
public class CheckBusyException : Exception;
public class Scheduler(IServiceScopeFactory scopes, ILogger<Scheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MonitorDb>();
                var due = await db.Monitors.Where(m => !m.Paused && m.NextCheckAt <= DateTimeOffset.UtcNow).Select(m => m.Id).ToListAsync(stoppingToken);
                foreach (var id in due)
                {
                    using var checkScope = scopes.CreateScope();
                    try { await checkScope.ServiceProvider.GetRequiredService<CheckService>().Run(id, true, stoppingToken); }
                    catch (CheckBusyException) { }
                }
            }
            catch (Exception) when (!stoppingToken.IsCancellationRequested) { log.LogWarning("Scheduler pass failed; retrying on next tick"); }
        }
    }
}
