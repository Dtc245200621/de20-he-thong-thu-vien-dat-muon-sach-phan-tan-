using System.Text.Json;
using Library.Contracts;
using RabbitMQ.Client;

namespace Library.Messaging;

public sealed class RabbitMqPublisher(RabbitMqOptions options) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;
    private readonly RabbitMqOptions _options = options;

    public Task PublishReservationAsync(ReservationRequestedMessage message, CancellationToken cancellationToken) =>
        PublishAsync(QueueNames.Reservations, JsonSerializer.SerializeToUtf8Bytes(message), null, cancellationToken);

    public Task RepublishForRetryAsync(string queue, byte[] body, int retryCount, CancellationToken cancellationToken) =>
        PublishAsync(queue, body, new Dictionary<string, object?> { ["x-retry-count"] = retryCount }, cancellationToken);

    private async Task PublishAsync(string queue, byte[] body, IDictionary<string, object?>? headers, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var channel = await GetChannelAsync(cancellationToken);
            var properties = new BasicProperties
            {
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Headers = headers
            };
            await channel.BasicPublishAsync("", queue, mandatory: true, basicProperties: properties, body: body, cancellationToken: cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_connection?.IsOpen == true && _channel?.IsOpen == true) return _channel;

        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();

        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        _connection = await factory.CreateConnectionAsync(cancellationToken);
        _channel = await _connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            cancellationToken);
        await _channel.QueueDeclareAsync(QueueNames.Reservations, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        await _channel.QueueDeclareAsync(QueueNames.DeadLetters, durable: true, exclusive: false, autoDelete: false, arguments: null, cancellationToken: cancellationToken);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        if (_connection is not null) await _connection.DisposeAsync();
        _gate.Dispose();
    }
}
