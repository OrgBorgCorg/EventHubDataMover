using System.Text;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHubDataMover
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;

        // === Replace with your actual values ===
        private const string sourceConnectionString = "<ENTER_YOUR_CONNECTION_STRING>";
        private const string sourceEventHubName = "<ENTER_YOURS>";

        private const string targetConnectionString = "<ENTER_YOURS>";
        private const string targetEventHubName = "<ENTER_YOURS>";

        private const string blobStorageConnectionString = "<ENTER_YOURS>";
        private const string blobContainerName = "<ENTER_YOURS>";

        private EventHubProducerClient _producerClient;
        private EventProcessorClient _processorClient;

        public Worker(ILogger<Worker> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var blobContainerClient = new BlobContainerClient(blobStorageConnectionString, blobContainerName);
            await blobContainerClient.CreateIfNotExistsAsync();

            _producerClient = new EventHubProducerClient(targetConnectionString, targetEventHubName);

            _processorClient = new EventProcessorClient(
                blobContainerClient,
                EventHubConsumerClient.DefaultConsumerGroupName,
                sourceConnectionString,
                sourceEventHubName
            );

            _processorClient.ProcessEventAsync += ProcessEventHandler;
            _processorClient.ProcessErrorAsync += ProcessErrorHandler;

            _logger.LogInformation("Starting Event Processor...");
            await _processorClient.StartProcessingAsync(stoppingToken);

            // Stop processing on shutdown
            stoppingToken.Register(async () =>
            {
                _logger.LogInformation("Stopping Event Processor...");
                await _processorClient.StopProcessingAsync();
                await _producerClient.DisposeAsync();
            });
        }

        private async Task ProcessEventHandler(ProcessEventArgs eventArgs)
        {
            try
            {
                string message = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());
                _logger.LogInformation($"Received: {message}");

                using EventDataBatch batch = await _producerClient.CreateBatchAsync();
                if (batch.TryAdd(new EventData(message)))
                {
                    await _producerClient.SendAsync(batch);
                    _logger.LogInformation("Message forwarded to target hub.");
                }
                else
                {
                    _logger.LogWarning("Message too large to fit in batch.");
                }

                await eventArgs.UpdateCheckpointAsync(eventArgs.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while processing event");
            }
        }

        private Task ProcessErrorHandler(ProcessErrorEventArgs args)
        {
            _logger.LogError(args.Exception, $"Error in partition '{args.PartitionId}'");
            return Task.CompletedTask;
        }
    }
}
