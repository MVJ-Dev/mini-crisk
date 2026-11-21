using System;
using System.Runtime.Caching;
using System.Threading.Tasks;
using Microsoft.Azure.KeyVault;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using MiniCRisk.Web.Config;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Azure Key Vault access using ADAL + X.509 client-certificate authentication.
    /// Secrets are cached for 60 minutes (same pattern as the real workload).
    /// AWS equivalent: Secrets Manager with the instance IAM role (no certificate, no AAD app).
    /// </summary>
    public class VaultSecretProvider
    {
        private static readonly MemoryCache Cache = new MemoryCache("vault-secrets");
        private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(60);

        private readonly string _vaultUri;
        private readonly string _clientId;
        private readonly string _certThumbprint;
        private readonly KeyVaultClient _client;

        public VaultSecretProvider()
        {
            _vaultUri = AppConfig.Require("Vault_Uri");
            _clientId = AppConfig.Require("Vault_ClientId");
            _certThumbprint = AppConfig.Require("Vault_CertThumbprint");
            _client = new KeyVaultClient(new KeyVaultClient.AuthenticationCallback(GetAccessTokenAsync));
        }

        public string GetSecret(string name)
        {
            var cached = Cache.Get(name) as string;
            if (cached != null)
            {
                return cached;
            }

            var bundle = _client.GetSecretAsync(_vaultUri, name).GetAwaiter().GetResult();
            Cache.Set(name, bundle.Value, DateTimeOffset.UtcNow.Add(Ttl));
            return bundle.Value;
        }

        private async Task<string> GetAccessTokenAsync(string authority, string resource, string scope)
        {
            // Needs the private key: the app pool identity must be able to read it
            var cert = CertificateStore.FindByThumbprint(_certThumbprint);
            var assertion = new ClientAssertionCertificate(_clientId, cert);
            var context = new AuthenticationContext(authority);
            var result = await context.AcquireTokenAsync(resource, assertion).ConfigureAwait(false);
            return result.AccessToken;
        }
    }
}
