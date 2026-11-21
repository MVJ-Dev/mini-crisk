using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MiniCRisk.Web.Config;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Signs requests to a third-party partner API with a certificate kept in the Windows certificate store
    /// (same idea as an OAuth client-certificate flow). It needs the PRIVATE KEY: if the IIS app pool
    /// identity cannot read it, this throws a CryptographicException ("Keyset does not exist").
    /// </summary>
    public static class PartnerRequestSigner
    {
        public class SignatureInfo
        {
            public string Thumbprint { get; set; }
            public string Algorithm { get; set; }
            public int SignatureLength { get; set; }
            public string SignedAtUtc { get; set; }
        }

        public static SignatureInfo SignPing()
        {
            var cert = CertificateStore.FindByThumbprint(AppConfig.Require("Partner_CertThumbprint"));
            var payload = "ping|" + DateTime.UtcNow.ToString("o");

            using (var rsa = cert.GetRSAPrivateKey())
            {
                if (rsa == null)
                {
                    throw new CryptographicException("Certificate " + cert.Thumbprint + " has no accessible RSA private key");
                }
                var signature = rsa.SignData(Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                return new SignatureInfo
                {
                    Thumbprint = cert.Thumbprint,
                    Algorithm = "RS256",
                    SignatureLength = signature.Length,
                    SignedAtUtc = DateTime.UtcNow.ToString("o")
                };
            }
        }
    }
}
