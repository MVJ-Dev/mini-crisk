using System;
using System.Linq;
using System.Runtime.Caching;
using System.Threading.Tasks;
using MiniCRisk.Web.Config;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Compliance reference list (blacklist) kept as a text file on an Azure File Share.
    /// Cached for 10 minutes. AWS equivalent: the same file in S3.
    /// </summary>
    public class ReferenceDataService
    {
        private static readonly MemoryCache Cache = new MemoryCache("reference-data");
        private readonly DocumentStorageManager _storage;

        public ReferenceDataService(DocumentStorageManager storage)
        {
            _storage = storage;
        }

        public async Task<bool> IsBlacklistedAsync(string name)
        {
            var lines = Cache.Get("blacklist") as string[];
            if (lines == null)
            {
                var text = await _storage.ReadShareTextAsync(
                    AppConfig.Get("Reference_ShareName", "reference-data"), "compliance", "blacklist.txt")
                    .ConfigureAwait(false);
                lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(l => l.Trim())
                            .ToArray();
                Cache.Set("blacklist", lines, DateTimeOffset.UtcNow.AddMinutes(10));
            }

            return lines.Any(l => string.Equals(l, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }
}
