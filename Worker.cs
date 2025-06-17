using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Producer;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHubDataMover
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private const string consumerGroup = EventHubConsumerClient.DefaultConsumerGroupName;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var sourceConnectionString = ${{ secrets.SOURCE_EXTENSIONS }} ;
            var targetConnectionString = ${{ secrets.TARGET_EXTENSION }};
            var sourceHubName = "source-hub";
            var targetHubName = "target-hub";

            await using var consumer = new EventHubConsumerClient(consumerGroup, sourceConnectionString, sourceHubName);
            await using var producer = new EventHubProducerClient(targetConnectionString, targetHubName);

            await foreach (PartitionEvent receivedEvent in consumer.ReadEventsAsync(stoppingToken))
            {
                string message = receivedEvent.Data.EventBody.ToString();
                _logger.LogInformation($"Received: {message}    ");

                using EventDataBatch eventBatch = await producer.CreateBatchAsync();
                eventBatch.TryAdd(new EventData(message));
                await producer.SendAsync(eventBatch);
            }
        }
    }
}