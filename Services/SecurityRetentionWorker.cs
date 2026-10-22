using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Data;

namespace PickleballBookingSystem.Services;

public sealed class SecurityRetentionWorker : BackgroundService
{
    private readonly IServiceScopeFactory _services;
    private readonly ILogger<SecurityRetentionWorker> _logger;

    public SecurityRetentionWorker(
        IServiceScopeFactory services,
        ILogger<SecurityRetentionWorker> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Run once at startup, then once per day
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // Null out raw IPs/UA after 90 days
                var ipPruned = await db.Database.ExecuteSqlRawAsync(
                    @"UPDATE security_events
                      SET ""IpAddress"" = NULL, ""UserAgent"" = NULL
                      WHERE ""OccurredAt"" < now() - interval '90 days'
                        AND (""IpAddress"" IS NOT NULL OR ""UserAgent"" IS NOT NULL);",
                    stoppingToken);

                // Delete info-level events after 180 days
                var infoDeleted = await db.Database.ExecuteSqlRawAsync(
                    @"DELETE FROM security_events
                      WHERE ""Severity"" = 'info'
                        AND ""OccurredAt"" < now() - interval '180 days';",
                    stoppingToken);

                _logger.LogInformation(
                    "Security retention: pruned {Pruned} IPs, deleted {Deleted} info events",
                    ipPruned, infoDeleted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Security retention worker failed");
            }

            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }
}