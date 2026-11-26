namespace Library.Messaging;

public sealed class RabbitMqOptions
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "library";
    public string Password { get; set; } = "library_dev";
    public string VirtualHost { get; set; } = "/";
}
