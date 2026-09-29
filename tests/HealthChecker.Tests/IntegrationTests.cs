using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HealthChecker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
namespace HealthChecker.Tests;

[Trait("Category", "Integration")]
public class IntegrationTests
{
    private readonly HttpClient api = new() { BaseAddress = new Uri(Environment.GetEnvironmentVariable("TEST_API_URL") ?? "http://localhost:8080"), Timeout = TimeSpan.FromSeconds(45) };
    private MonitorDb Database() => new(new DbContextOptionsBuilder<MonitorDb>().UseNpgsql(Environment.GetEnvironmentVariable("TEST_DATABASE") ?? throw new InvalidOperationException("Run integration tests with scripts/test.sh")).Options);
    private async Task<MonitorView> Create(string path = "healthy")
    {
        var response = await api.PostAsJsonAsync("/api/monitors", new { url = $"http://demo-target:8080/{path}", email = $"test-{Guid.NewGuid():N}@example.test" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MonitorView>())!;
    }
    private async Task<CheckResult[]> History(Guid id) => (await api.GetFromJsonAsync<CheckResult[]>($"/api/monitors/{id}/checks"))!;
    private static async Task Eventually(Func<Task<bool>> condition, int seconds = 20)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        while (!await condition()) await Task.Delay(100, timeout.Token);
    }
    [Fact]
    public async Task CompleteLifecyclePersistsResultsAndDeliversEmail()
    {
        var m = await Create();
        try
        {
            var first = Assert.Single(await History(m.Id));
            Assert.True(first.Healthy); Assert.Equal(200, first.StatusCode); Assert.Equal("Sent", first.EmailStatus);
            using var db = Database();
            Assert.True(await db.Checks.AnyAsync(c => c.Id == first.Id));
            using var mail = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("TEST_MAILPIT_URL") ?? "http://localhost:8025") };
            await Eventually(async () =>
            {
                var response = await mail.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString(first.Id.ToString())}");
                return response.GetProperty("messages").GetArrayLength() == 1;
            });
            Assert.Equal(HttpStatusCode.OK, (await api.PatchAsJsonAsync($"/api/monitors/{m.Id}", new { paused = true })).StatusCode);
            Assert.True((await api.GetFromJsonAsync<MonitorView>($"/api/monitors/{m.Id}"))!.Paused);
            Assert.Equal(HttpStatusCode.OK, (await api.PostAsync($"/api/monitors/{m.Id}/checks", null)).StatusCode);
            Assert.Equal(2, (await History(m.Id)).Length);
            Assert.Equal(HttpStatusCode.OK, (await api.PatchAsJsonAsync($"/api/monitors/{m.Id}", new { paused = false })).StatusCode);
            await Eventually(async () => (await History(m.Id)).Length == 3);
            Assert.Equal(HttpStatusCode.NoContent, (await api.DeleteAsync($"/api/monitors/{m.Id}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await api.GetAsync($"/api/monitors/{m.Id}/checks")).StatusCode);
            Assert.False(await db.Checks.AnyAsync(c => c.MonitorId == m.Id));
        }
        finally { await api.DeleteAsync($"/api/monitors/{m.Id}"); }
    }
    [Theory]
    [InlineData("failing", false, "HTTP 503")]
    [InlineData("redirect", true, null)]
    [InlineData("redirect-private", false, "Destination blocked by URL policy")]
    [InlineData("loop", false, "Too many redirects")]
    [InlineData("slow", false, "Timed out after 10 seconds")]
    public async Task DeterministicHttpScenarios(string path, bool healthy, string? error)
    {
        var m = await Create(path);
        try { var c = Assert.Single(await History(m.Id)); Assert.Equal(healthy, c.Healthy); Assert.Equal(error, c.Error); Assert.Equal("Sent", c.EmailStatus); }
        finally { await api.DeleteAsync($"/api/monitors/{m.Id}"); }
    }
    [Theory]
    [InlineData("http://127.0.0.1:8080", "ok@example.test", "url")]
    [InlineData("http://[::1]:8080", "ok@example.test", "url")]
    [InlineData("http://user:password@example.com", "ok@example.test", "url")]
    [InlineData("http://demo-target:8080/healthy", "bad", "email")]
    public async Task ValidationUsesProblemDetails(string url, string email, string field)
    {
        var response = await api.PostAsJsonAsync("/api/monitors", new { url, email });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("errors").TryGetProperty(field, out _));
    }
    [Fact]
    public async Task SchedulerSkipsPausedMonitorsAndCatchesUpOnce()
    {
        var m = await Create();
        try
        {
            await api.PatchAsJsonAsync($"/api/monitors/{m.Id}", new { paused = true });
            await using var db = Database();
            await db.Monitors.Where(x => x.Id == m.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.NextCheckAt, DateTimeOffset.UtcNow.AddDays(-1)));
            var service = new CheckService(db, new FixedChecker(), new SuccessfulMailer(), new MonitorLocks(), NullLogger<CheckService>.Instance);
            Assert.Null(await service.Run(m.Id, true, CancellationToken.None));
            Assert.Single(await History(m.Id));
            await api.PatchAsJsonAsync($"/api/monitors/{m.Id}", new { paused = false });
            await Eventually(async () => (await History(m.Id)).Length == 2);
            var updated = await api.GetFromJsonAsync<MonitorView>($"/api/monitors/{m.Id}");
            Assert.True(updated!.NextCheckAt > DateTimeOffset.UtcNow.AddMinutes(4));
        }
        finally { await api.DeleteAsync($"/api/monitors/{m.Id}"); }
    }
    [Fact]
    public async Task MailFailureDoesNotChangeWebsiteHealthAndLocksPreventOverlap()
    {
        var m = await Create();
        try
        {
            await api.PatchAsJsonAsync($"/api/monitors/{m.Id}", new { paused = true });
            await using var db = Database();
            var locks = new MonitorLocks();
            var service = new CheckService(db, new FixedChecker(), new FailedMailer(), locks, NullLogger<CheckService>.Instance);
            var result = await service.Run(m.Id, false, CancellationToken.None);
            Assert.True(result!.Healthy); Assert.Equal("Failed", result.EmailStatus);
            Assert.Equal("Failed", (await History(m.Id))[0].EmailStatus);
            await locks.For(m.Id).WaitAsync();
            try { await Assert.ThrowsAsync<CheckBusyException>(() => service.Run(m.Id, false, CancellationToken.None)); }
            finally { locks.For(m.Id).Release(); }
            await locks.For(m.Id).WaitAsync();
            var initialCheck = service.Run(m.Id, false, CancellationToken.None, waitForLock: true);
            Assert.False(initialCheck.IsCompleted);
            locks.For(m.Id).Release();
            Assert.True((await initialCheck)!.Healthy);
        }
        finally { await api.DeleteAsync($"/api/monitors/{m.Id}"); }
    }
    [Fact]
    public async Task HealthAndOpenApiAreAvailable()
    {
        Assert.True((await api.GetAsync("/health/live")).IsSuccessStatusCode);
        Assert.True((await api.GetAsync("/health/ready")).IsSuccessStatusCode);
        var spec = await api.GetFromJsonAsync<JsonElement>("/openapi/v1.json");
        Assert.True(spec.GetProperty("paths").TryGetProperty("/api/monitors", out _));
    }
    private class FixedChecker : IWebsiteChecker { public Task<CheckResult> Check(string url, CancellationToken ct) => Task.FromResult(new CheckResult { Healthy = true, StatusCode = 200 }); }
    private class SuccessfulMailer : IResultMailer { public Task Send(SiteMonitor m, CheckResult r, CancellationToken ct) => Task.CompletedTask; }
    private class FailedMailer : IResultMailer { public Task Send(SiteMonitor m, CheckResult r, CancellationToken ct) => throw new IOException("Deliberate test mail failure"); }
}
