using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;

namespace MiniCRisk.Functions
{
    /// <summary>
    /// Timer-triggered function: every day at 02:00 UTC it writes a CSV summary of credit requests per status.
    /// App settings (in Azure these are Key Vault references: @Microsoft.KeyVault(SecretUri=...)):
    ///   MySqlConnection, StorageConnection, ReportsContainer
    /// AWS target: EventBridge Scheduler -> Lambda, secrets from Secrets Manager, output to S3.
    /// </summary>
    public class NightlyReport
    {
        private readonly ILogger<NightlyReport> _logger;

        public NightlyReport(ILogger<NightlyReport> logger)
        {
            _logger = logger;
        }

        [Function("NightlyReport")]
        public async Task Run([TimerTrigger("0 0 2 * * *")] TimerInfo timer)
        {
            var mysql = Environment.GetEnvironmentVariable("MySqlConnection");
            var storage = Environment.GetEnvironmentVariable("StorageConnection");
            var container = Environment.GetEnvironmentVariable("ReportsContainer") ?? "reports";

            var csv = new StringBuilder("status,count\n");
            using (var conn = new MySqlConnection(mysql))
            {
                await conn.OpenAsync();
                using (var cmd = new MySqlCommand("SELECT status, COUNT(*) FROM credit_requests GROUP BY status ORDER BY status", conn))
                using (var reader = await cmd.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        csv.Append(reader.GetString(0)).Append(',')
                           .Append(Convert.ToInt64(reader.GetValue(1)).ToString(CultureInfo.InvariantCulture)).Append('\n');
                    }
                }
            }

            var blobName = "credit-requests-" + DateTime.UtcNow.ToString("yyyyMMdd") + ".csv";
            var containerClient = new BlobContainerClient(storage, container);
            await containerClient.CreateIfNotExistsAsync();
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv.ToString())))
            {
                await containerClient.GetBlobClient(blobName).UploadAsync(stream, overwrite: true);
            }

            _logger.LogInformation("NightlyReport written: {Blob}", blobName);
        }
    }
}
