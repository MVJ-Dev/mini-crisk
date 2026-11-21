using System;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage.Queue;
using MiniCRisk.Web.Config;
using MiniCRisk.Web.Models;
using Newtonsoft.Json;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Azure Storage Queue wrapper. Notes for the migration to SQS:
    ///  - Storage Queues have no native dead-letter queue: poison handling is manual (DequeueCount + a second queue).
    ///  - Both are at-least-once: consumers must be idempotent.
    ///  - Visibility timeout exists in both, but SQS adds long polling, DLQ redrive and FIFO options.
    /// </summary>
    public class JobQueueManager
    {
        public const int MaxDequeueCount = 5;

        private readonly CloudQueue _queue;
        private readonly CloudQueue _poison;

        public JobQueueManager(VaultSecretProvider vault)
        {
            var account = Microsoft.WindowsAzure.Storage.CloudStorageAccount.Parse(
                vault.GetSecret("storage-connection-string"));
            var client = account.CreateCloudQueueClient();
            var name = AppConfig.Get("Storage_QueueName", "jobs");
            _queue = client.GetQueueReference(name);
            _poison = client.GetQueueReference(name + "-poison");
        }

        public async Task EnqueueAsync(JobMessage message)
        {
            await _queue.CreateIfNotExistsAsync().ConfigureAwait(false);
            await _queue.AddMessageAsync(new CloudQueueMessage(JsonConvert.SerializeObject(message)))
                .ConfigureAwait(false);
        }

        public async Task<CloudQueueMessage> DequeueAsync(TimeSpan visibilityTimeout)
        {
            await _queue.CreateIfNotExistsAsync().ConfigureAwait(false);
            return await _queue.GetMessageAsync(visibilityTimeout, null, null).ConfigureAwait(false);
        }

        public Task CompleteAsync(CloudQueueMessage message)
        {
            return _queue.DeleteMessageAsync(message);
        }

        public async Task MoveToPoisonAsync(CloudQueueMessage message)
        {
            await _poison.CreateIfNotExistsAsync().ConfigureAwait(false);
            await _poison.AddMessageAsync(new CloudQueueMessage(message.AsString)).ConfigureAwait(false);
            await _queue.DeleteMessageAsync(message).ConfigureAwait(false);
        }

        public async Task<int> GetApproximateCountAsync()
        {
            await _queue.CreateIfNotExistsAsync().ConfigureAwait(false);
            await _queue.FetchAttributesAsync().ConfigureAwait(false);
            return _queue.ApproximateMessageCount ?? 0;
        }
    }
}
