using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.File;
using MiniCRisk.Web.Config;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Azure Blob Storage (documents + SAS links) and Azure File Share (reference text files).
    /// AWS equivalent: S3 (PutObject, pre-signed URLs) - the File Share content also moves to S3.
    /// </summary>
    public class DocumentStorageManager
    {
        private readonly CloudStorageAccount _account;
        private readonly CloudBlobContainer _container;

        public DocumentStorageManager(VaultSecretProvider vault)
        {
            _account = CloudStorageAccount.Parse(vault.GetSecret("storage-connection-string"));
            _container = _account.CreateCloudBlobClient()
                .GetContainerReference(AppConfig.Get("Storage_DocumentsContainer", "documents"));
        }

        public async Task<string> UploadAsync(string path, Stream content, string contentType)
        {
            await _container.CreateIfNotExistsAsync().ConfigureAwait(false);
            var blob = _container.GetBlockBlobReference(path);
            blob.Properties.ContentType = contentType;
            await blob.UploadFromStreamAsync(content).ConfigureAwait(false);
            return blob.Name;
        }

        public async Task<byte[]> DownloadAsync(string path)
        {
            var blob = _container.GetBlockBlobReference(path);
            using (var ms = new MemoryStream())
            {
                await blob.DownloadToStreamAsync(ms).ConfigureAwait(false);
                return ms.ToArray();
            }
        }

        /// <summary>Read-only SAS URL valid for the given time (AWS: pre-signed GET URL).</summary>
        public string GetReadSasUrl(string path, TimeSpan validFor)
        {
            var blob = _container.GetBlockBlobReference(path);
            var policy = new SharedAccessBlobPolicy
            {
                Permissions = SharedAccessBlobPermissions.Read,
                SharedAccessExpiryTime = DateTimeOffset.UtcNow.Add(validFor)
            };
            return blob.Uri + blob.GetSharedAccessSignature(policy);
        }

        /// <summary>Reads a small text file from an Azure File Share (AWS: S3 GetObject).</summary>
        public async Task<string> ReadShareTextAsync(string shareName, string directory, string fileName)
        {
            var share = _account.CreateCloudFileClient().GetShareReference(shareName);
            var dir = share.GetRootDirectoryReference().GetDirectoryReference(directory);
            var file = dir.GetFileReference(fileName);
            return await file.DownloadTextAsync().ConfigureAwait(false);
        }
    }
}
