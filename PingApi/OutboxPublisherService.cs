using System.Text.Json;
using Library.Contracts;
using Library.Data;
using Library.Messaging;
using Microsoft.EntityFrameworkCore;

public sealed class OutboxPublisherService(IServiceScopeFactory scopeFactory, RabbitMqPublisher publisher, ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
                await using var db = await factory.CreateDbContextAsync(stoppingToken);
                var pending = await db.OutboxMessages.Where(x => x.PublishedAt == null).OrderBy(x => x.CreatedAt).Take(50).ToListAsync(stoppingToken);
                foreach (var item in pending)
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<ReservationRequestedMessage>(item.Payload);
                        if (message is null) { item.Attempts++; continue; }
                        await publisher.PublishReservationAsync(message, stoppingToken);
                        item.PublishedAt = DateTimeOffset.UtcNow;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        item.Attempts++;
                        logger.LogWarning(ex, "Outbox publish failed for {OutboxId}; attempt {Attempt}", item.Id, item.Attempts);
                        break;
                    }
                }
                await db.SaveChangesAsync(stoppingToken);
                await Task.Delay(pending.Count == 0 ? TimeSpan.FromSeconds(2) : TimeSpan.FromMilliseconds(300), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox publisher iteration failed");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
