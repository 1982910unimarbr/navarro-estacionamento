using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;

namespace Backend.Services
{
    // Placeholder background service. MQTT implementation will be added next.
    public class MqttSubscriber : BackgroundService
    {
        private readonly ILogger<MqttSubscriber> _logger;
        public MqttSubscriber(ILogger<MqttSubscriber> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("MqttSubscriber placeholder started. MQTT integration pending.");
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(5000, stoppingToken);
            }
        }
    }
}
