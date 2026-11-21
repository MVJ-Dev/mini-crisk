using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using MiniCRisk.Web.Services;

namespace MiniCRisk.Web.Controllers
{
    public class DocumentsController : ApiController
    {
        /// <summary>Multipart upload of one file attached to a credit request (stored in Blob Storage).</summary>
        [HttpPost, Route("api/credit-requests/{requestId:long}/documents")]
        public async Task<IHttpActionResult> Upload(long requestId)
        {
            if (!Request.Content.IsMimeMultipartContent())
            {
                return StatusCode(HttpStatusCode.UnsupportedMediaType);
            }

            var request = await ServiceRegistry.Repository.GetAsync(requestId);
            if (request == null)
            {
                return NotFound();
            }

            var multipart = await Request.Content.ReadAsMultipartAsync(new MultipartMemoryStreamProvider());
            var part = multipart.Contents.FirstOrDefault();
            if (part == null)
            {
                return BadRequest("a file part is required");
            }

            var fileName = Path.GetFileName((part.Headers.ContentDisposition.FileName ?? "upload.bin").Trim('"'));
            var contentType = part.Headers.ContentType != null && part.Headers.ContentType.MediaType != null
                ? part.Headers.ContentType.MediaType
                : "application/octet-stream";
            var bytes = await part.ReadAsByteArrayAsync();

            var blobPath = requestId + "/" + Guid.NewGuid().ToString("N") + "-" + fileName;
            using (var stream = new MemoryStream(bytes))
            {
                await ServiceRegistry.Storage.UploadAsync(blobPath, stream, contentType);
            }

            var documentId = await ServiceRegistry.Repository.AddDocumentAsync(
                requestId, blobPath, fileName, contentType, bytes.LongLength);

            return Created("api/documents/" + documentId, new { id = documentId, path = blobPath, size = bytes.Length });
        }

        /// <summary>Returns a time-limited download link (Azure: SAS URL; AWS: pre-signed URL).</summary>
        [HttpGet, Route("api/documents/{documentId:long}/link")]
        public async Task<IHttpActionResult> GetLink(long documentId, int minutes = 15)
        {
            if (minutes < 1 || minutes > 60)
            {
                minutes = 15;
            }

            var doc = await ServiceRegistry.Repository.GetDocumentAsync(documentId);
            if (doc == null)
            {
                return NotFound();
            }

            var url = ServiceRegistry.Storage.GetReadSasUrl(doc.BlobPath, TimeSpan.FromMinutes(minutes));
            return Ok(new { url, expiresInMinutes = minutes, fileName = doc.FileName });
        }
    }
}
