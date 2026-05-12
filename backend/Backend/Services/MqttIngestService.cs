using System.Buffers;
using System.Text;
using System.Text.Json;
using Backend.DTOs;
using MQTTnet;
using MQTTnet.Protocol;

namespace Backend.Services;

public class MqttIngestService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<MqttIngestService> _logger;
    private readonly JsonSerializerOptions _jsonOptions;
    private IMqttClient? _client;
    private TaskCompletionSource<bool>? _disconnectTcs;

    public MqttIngestService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<MqttIngestService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var mqttHost = _configuration["Mqtt:Host"] ?? Environment.GetEnvironmentVariable("MQTT_HOST") ?? "localhost";
        var mqttPort = int.TryParse(_configuration["Mqtt:Port"] ?? Environment.GetEnvironmentVariable("MQTT_PORT"), out var port)
            ? port
            : 1883;

        var factory = new MqttClientFactory();
        _client = factory.CreateMqttClient();
        _client.ConnectedAsync += args =>
        {
            _logger.LogInformation("MQTT connected to {Host}:{Port}", mqttHost, mqttPort);
            return Task.CompletedTask;
        };
        _client.DisconnectedAsync += args =>
        {
            _logger.LogWarning("MQTT disconnected: {Reason}", args.ReasonString);
            _disconnectTcs?.TrySetResult(true);
            return Task.CompletedTask;
        };
        _client.ApplicationMessageReceivedAsync += HandleMessageAsync;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _disconnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var options = new MqttClientOptionsBuilder()
                    .WithClientId($"backend-ingest-{Guid.NewGuid():N}")
                    .WithTcpServer(mqttHost, mqttPort)
                    .Build();

                await _client.ConnectAsync(options, stoppingToken);
                await _client.SubscribeAsync("campus/parking/sectors/+/spots/+/events", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                await _client.SubscribeAsync("campus/parking/sectors/+/gateway/status", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);
                await _client.SubscribeAsync("campus/parking/sectors/+/incidents", MqttQualityOfServiceLevel.AtLeastOnce, stoppingToken);

                await Task.WhenAny(_disconnectTcs.Task, Task.Delay(Timeout.Infinite, stoppingToken));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT ingest loop error");
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }

        if (_client.IsConnected)
        {
            await _client.DisconnectAsync();
        }
    }

    private async Task HandleMessageAsync(MqttApplicationMessageReceivedEventArgs args)
    {
        var topic = args.ApplicationMessage.Topic;
        var payload = args.ApplicationMessage.Payload;
        var payloadText = payload.IsEmpty
            ? string.Empty
            : Encoding.UTF8.GetString(payload.ToArray());

        if (string.IsNullOrWhiteSpace(payloadText))
        {
            _logger.LogWarning("MQTT message with empty payload on {Topic}", topic);
            return;
        }

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var ingest = scope.ServiceProvider.GetRequiredService<IParkingIngestService>();

            if (topic.EndsWith("/gateway/status", StringComparison.OrdinalIgnoreCase))
            {
                var status = JsonSerializer.Deserialize<GatewayStatusRequest>(payloadText, _jsonOptions);
                if (status == null)
                {
                    _logger.LogWarning("MQTT gateway payload invalid on {Topic}", topic);
                    return;
                }
                await ingest.HandleGatewayStatusAsync(status);
                return;
            }

            if (topic.EndsWith("/incidents", StringComparison.OrdinalIgnoreCase))
            {
                var incident = JsonSerializer.Deserialize<IncidentRequest>(payloadText, _jsonOptions);
                if (incident == null)
                {
                    _logger.LogWarning("MQTT incident payload invalid on {Topic}", topic);
                    return;
                }
                await ingest.HandleIncidentAsync(incident);
                return;
            }

            var ev = JsonSerializer.Deserialize<EventRequest>(payloadText, _jsonOptions);
            if (ev == null)
            {
                _logger.LogWarning("MQTT event payload invalid on {Topic}", topic);
                return;
            }
            await ingest.HandleEventAsync(ev);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MQTT message processing error on {Topic}", topic);
        }
    }
}
