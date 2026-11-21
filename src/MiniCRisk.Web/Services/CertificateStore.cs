using System;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Loads X.509 certificates from the Windows certificate store (LocalMachine\My).
    /// On Azure Cloud Services the .csdef/.cscfg installs the certificates; on EC2 YOU must import them
    /// (PFX from Secrets Manager / user data) AND grant the IIS app pool identity access to the private key.
    /// </summary>
    public static class CertificateStore
    {
        public static X509Certificate2 FindByThumbprint(string thumbprint, StoreLocation location = StoreLocation.LocalMachine)
        {
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                throw new ArgumentException("Certificate thumbprint is empty", "thumbprint");
            }

            // Thumbprints copied from the MMC snap-in often carry invisible characters
            var clean = Regex.Replace(thumbprint, "[^0-9A-Fa-f]", string.Empty).ToUpperInvariant();

            var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            try
            {
                var found = store.Certificates.Find(X509FindType.FindByThumbprint, clean, false);
                if (found.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Certificate " + clean + " not found in " + location + "\\My");
                }
                return found[0];
            }
            finally
            {
                store.Close();
            }
        }
    }
}
