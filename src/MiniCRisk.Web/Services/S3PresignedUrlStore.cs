using System;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Already-working S3 pre-signed URL pattern (not wired to any endpoint yet), mirroring the real workload
    /// where the AWS SDK is referenced and the pattern exists. Use it as the template when replacing Blob SAS.
    /// Credentials come from the default chain (instance role on EC2).
    /// </summary>
    public class S3PresignedUrlStore
    {
        private readonly RegionEndpoint _region;

        public S3PresignedUrlStore(RegionEndpoint region)
        {
            _region = region;
        }

        public string GetDownloadUrl(string bucket, string key, TimeSpan validFor)
        {
            using (var s3 = new AmazonS3Client(_region))
            {
                var request = new GetPreSignedUrlRequest
                {
                    BucketName = bucket,
                    Key = key,
                    Verb = HttpVerb.GET,
                    Expires = DateTime.UtcNow.Add(validFor)
                };
                return s3.GetPreSignedURL(request);
            }
        }
    }
}
