using System.Text.Json;
using Library.Contracts;
using Library.Data;
using Library.Messaging;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

public sealed class ReservationConsumer(IServiceScopeFactory scopes, RabbitMqOptions options, RabbitMqPublisher publisher, ILogger<ReservationConsumer> logger) : BackgroundService
{
    private IConnection? _connection;
    private IChannel? _channel;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = options.HostName, Port = options.Port, UserName = options.UserName, Password = options.Password,
            VirtualHost = options.VirtualHost, AutomaticRecoveryEnabled = true, NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection = await factory.CreateConnectionAsync(stoppingToken);
                _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await _channel.QueueDeclareAsync(QueueNames.Reservations, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await _channel.QueueDeclareAsync(QueueNames.DeadLetters, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: stoppingToken);
                await _channel.BasicQosAsync(0, 1, false, stoppingToken);
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.ReceivedAsync += async (_, ea) => await ProcessAsync(ea, stoppingToken);
                await _channel.BasicConsumeAsync(QueueNames.Reservations, autoAck: false, consumer, stoppingToken);
                // Keep the hosted service alive for the lifetime of the channel.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                logger.LogError(ex, "RabbitMQ consumer disconnected; retrying");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                if (_channel is not null) { await _channel.DisposeAsync(); _channel = null; }
                if (_connection is not null) { await _connection.DisposeAsync(); _connection = null; }
            }
        }
    }

    private async Task ProcessAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var channel = _channel;
        if (channel is null) return;
        try
        {
            var message = JsonSerializer.Deserialize<ReservationRequestedMessage>(ea.Body.Span);
            if (message is null) throw new JsonException("Empty reservation message");
            await using var scope = scopes.CreateAsyncScope();
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LibraryDbContext>>();
            await using var db = await factory.CreateDbContextAsync(ct);
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var reservation = await db.Reservations.FromSqlInterpolated($"SELECT * FROM \"Reservations\" WHERE \"Id\" = {message.ReservationId} FOR UPDATE").FirstOrDefaultAsync(ct);
            if (reservation is null || reservation.Status != ReservationStatus.Pending)
            {
                await tx.CommitAsync(ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
                return;
            }

            var updated = await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Books\" SET \"AvailableCopies\" = \"AvailableCopies\" - 1 WHERE \"Id\" = {message.BookId} AND \"AvailableCopies\" > 0", ct);
            reservation.Status = updated == 1 ? ReservationStatus.Reserved : ReservationStatus.Rejected;
            reservation.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Reservation message processing failed");
            var retries = GetRetryCount(ea.BasicProperties.Headers) + 1;
            try
            {
                if (retries < 5) await Task.Delay(TimeSpan.FromMilliseconds(250 * retries), ct);
                await publisher.RepublishForRetryAsync(retries >= 5 ? QueueNames.DeadLetters : QueueNames.Reservations, ea.Body.ToArray(), retries, ct);
                await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
            }
            catch { await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: true, cancellationToken: ct); }
        }
    }

    private static int GetRetryCount(IDictionary<string, object?>? headers)
    {
        if (headers is null || !headers.TryGetValue("x-retry-count", out var value)) return 0;
        return value switch { byte b => b, int i => i, long l => (int)l, _ => 0 };
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_channel is not null) await _channel.CloseAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
