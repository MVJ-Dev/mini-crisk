using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace MiniCRisk.Functions
{
    /// <summary>
    /// Queue-triggered function: messages on "ingest-documents" (JSON: {"requestId":123}) are forwarded
    /// to the web API, which creates the scoring job.
    /// Settings: StorageConnection (queue connection), WebApiBaseUrl.
    /// AWS target: SQS queue as Lambda event source (batch size, partial batch responses, DLQ via redrive policy).
    /// </summary>
    public class DocumentIngest
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly ILogger<DocumentIngest> _logger;

        public DocumentIngest(ILogger<DocumentIngest> logger)
        {
            _logger = logger;
        }

        [Function("DocumentIngest")]
        public async Task Run([QueueTrigger("ingest-documents", Connection = "StorageConnection")] string message)
        {
            var baseUrl = Environment.GetEnvironmentVariable("WebApiBaseUrl");
            _logger.LogInformation("DocumentIngest received: {Message}", message);

            using (var content = new StringContent(message, Encoding.UTF8, "application/json"))
            using (var response = await Http.PostAsync(baseUrl.TrimEnd('/') + "/api/credit-requests/ingest", content))
            {
                // Throwing makes the runtime retry the message; after 5 attempts it lands in ingest-documents-poison
                response.EnsureSuccessStatusCode();
            }
        }
    }
}
