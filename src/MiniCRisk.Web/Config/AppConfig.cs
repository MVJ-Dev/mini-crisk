using System;
using Microsoft.WindowsAzure;

namespace MiniCRisk.Web.Config
{
    /// <summary>
    /// Reads settings through CloudConfigurationManager:
    /// inside an Azure role it reads the .cscfg; anywhere else it falls back to Web.config appSettings.
    /// (On AWS there is no .cscfg: settings come from Web.config / environment / Secrets Manager.)
    /// </summary>
    public static class AppConfig
    {
        public static string Get(string key, string fallback = null)
        {
            var value = CloudConfigurationManager.GetSetting(key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        public static string Require(string key)
        {
            var value = Get(key);
            if (string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException("Missing required setting: " + key);
            }
            return value;
        }
    }
}
