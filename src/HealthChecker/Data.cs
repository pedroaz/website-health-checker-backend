using Microsoft.EntityFrameworkCore;
namespace HealthChecker;

public class SiteMonitor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Url { get; set; } = "";
    public string Email { get; set; } = "";
    public bool Paused { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset NextCheckAt { get; set; } = DateTimeOffset.UtcNow;
    public List<CheckResult> Checks { get; set; } = [];
}
public class CheckResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid MonitorId { get; set; }
    public DateTimeOffset CheckedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Healthy { get; set; }
    public int? StatusCode { get; set; }
    public long ResponseTimeMs { get; set; }
    public string? Error { get; set; }
    public string EmailStatus { get; set; } = "Pending";
}
public class MonitorDb(DbContextOptions<MonitorDb> options) : DbContext(options)
{
    public DbSet<SiteMonitor> Monitors => Set<SiteMonitor>();
    public DbSet<CheckResult> Checks => Set<CheckResult>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<SiteMonitor>().HasMany(m => m.Checks).WithOne().HasForeignKey(c => c.MonitorId).OnDelete(DeleteBehavior.Cascade);
        model.Entity<SiteMonitor>().Property(m => m.Url).HasMaxLength(2048);
        model.Entity<SiteMonitor>().Property(m => m.Email).HasMaxLength(254);
        model.Entity<CheckResult>().HasIndex(c => new { c.MonitorId, c.CheckedAt });
    }
}
public record CreateMonitor(string Url, string Email);
public record UpdateMonitor(bool Paused);
public record MonitorView(Guid Id, string Url, string Email, bool Paused, DateTimeOffset CreatedAt, DateTimeOffset NextCheckAt, CheckResult? LatestCheck)
{
    public static MonitorView From(SiteMonitor m) => new(m.Id, m.Url, m.Email, m.Paused, m.CreatedAt, m.NextCheckAt, m.Checks.OrderByDescending(c => c.CheckedAt).FirstOrDefault());
}
