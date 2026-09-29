using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using HealthChecker;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<MonitorDb>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("Database") ?? "Host=localhost;Database=health;Username=health;Password=local-demo-password"));
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddSingleton<UrlPolicy>();
builder.Services.AddSingleton<MonitorLocks>();
builder.Services.AddScoped<IWebsiteChecker, WebsiteChecker>();
builder.Services.AddScoped<IResultMailer, ResultMailer>();
builder.Services.AddScoped<CheckService>();
builder.Services.AddHostedService<Scheduler>();
var app = builder.Build();
app.UseExceptionHandler();
app.MapOpenApi();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (MonitorDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
app.MapGet("/api/monitors", async (MonitorDb db) => (await db.Monitors.AsNoTracking().Include(m => m.Checks.OrderByDescending(c => c.CheckedAt).Take(1)).OrderByDescending(m => m.CreatedAt).ToListAsync()).Select(MonitorView.From));
app.MapGet("/api/monitors/{id:guid}", async (Guid id, MonitorDb db) =>
{
    var monitor = await db.Monitors.AsNoTracking().Include(m => m.Checks.OrderByDescending(c => c.CheckedAt).Take(1)).FirstOrDefaultAsync(m => m.Id == id);
    return monitor is null ? Results.NotFound() : Results.Ok(MonitorView.From(monitor));
});
app.MapPost("/api/monitors", async (CreateMonitor input, MonitorDb db, UrlPolicy policy, CheckService checks) =>
{
    var errors = new Dictionary<string, string[]>();
    Uri? uri = null;
    try { uri = UrlPolicy.Parse(input.Url); }
    catch (ArgumentException e) { errors["url"] = [e.Message]; }
    if (string.IsNullOrWhiteSpace(input.Email) || input.Email.Length > 254 || input.Email.Contains('\r') || input.Email.Contains('\n') || !new EmailAddressAttribute().IsValid(input.Email))
        errors["email"] = ["Enter a valid email address (maximum 254 characters)."];
    if (errors.Count > 0) return Results.ValidationProblem(errors);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    try { await policy.Resolve(uri!, deadline.Token); }
    catch (ArgumentException e) { return Results.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [e.Message] }); }
    catch (Exception e) when (e is SocketException or OperationCanceledException) { /* Persist unresolvable sites so their failed checks can be demonstrated. */ }
    var monitor = new SiteMonitor { Url = uri!.AbsoluteUri, Email = input.Email.Trim(), NextCheckAt = DateTimeOffset.UtcNow.AddMinutes(5) };
    db.Monitors.Add(monitor);
    await db.SaveChangesAsync();
    await checks.Run(monitor.Id, false, CancellationToken.None, waitForLock: true);
    return Results.Created($"/api/monitors/{monitor.Id}", MonitorView.From(monitor));
});
app.MapPost("/api/monitors/{id:guid}/checks", async (Guid id, CheckService checks) =>
{
    try { var result = await checks.Run(id, false, CancellationToken.None); return result is null ? Results.NotFound() : Results.Ok(result); }
    catch (CheckBusyException) { return Results.Problem(statusCode: 409, title: "A check or update is already in progress."); }
});
app.MapGet("/api/monitors/{id:guid}/checks", async (Guid id, MonitorDb db) =>
    !await db.Monitors.AnyAsync(m => m.Id == id) ? Results.NotFound() : Results.Ok(await db.Checks.AsNoTracking().Where(c => c.MonitorId == id).OrderByDescending(c => c.CheckedAt).Take(50).ToListAsync()));
app.MapPatch("/api/monitors/{id:guid}", async (Guid id, UpdateMonitor input, MonitorDb db, MonitorLocks locks) =>
{
    var gate = locks.For(id); await gate.WaitAsync();
    try
    {
        var monitor = await db.Monitors.FindAsync(id);
        if (monitor is null) return Results.NotFound();
        if (monitor.Paused && !input.Paused) monitor.NextCheckAt = DateTimeOffset.UtcNow;
        monitor.Paused = input.Paused;
        await db.SaveChangesAsync(); return Results.Ok(MonitorView.From(monitor));
    }
    finally { gate.Release(); }
});
app.MapDelete("/api/monitors/{id:guid}", async (Guid id, MonitorDb db, MonitorLocks locks) =>
{
    var gate = locks.For(id); await gate.WaitAsync();
    try
    {
        var monitor = await db.Monitors.FindAsync(id);
        if (monitor is null) return Results.NotFound();
        db.Monitors.Remove(monitor); await db.SaveChangesAsync(); return Results.NoContent();
    }
    finally { gate.Release(); }
});
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<MonitorDb>().Database.MigrateAsync();
app.Run();
