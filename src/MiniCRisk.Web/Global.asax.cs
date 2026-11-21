using System;
using System.Diagnostics;
using System.Net;
using System.Web;
using System.Web.Http;
using MiniCRisk.Web.Config;
using MiniCRisk.Web.Services;

namespace MiniCRisk.Web
{
    public class WebApiApplication : HttpApplication
    {
        protected void Application_Start()
        {
            // TLS 1.2 for outbound calls (Key Vault, Storage, partner APIs, scoring host)
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            GlobalConfiguration.Configure(WebApiConfig.Register);

            // Background consumer for the jobs queue (runs inside the web app process)
            if (string.Equals(AppConfig.Get("Worker_Enabled", "true"), "true", StringComparison.OrdinalIgnoreCase))
            {
                QueueWorker.Start();
                Trace.TraceInformation("QueueWorker started");
            }
        }

        protected void Application_End()
        {
            QueueWorker.Stop();
        }
    }
}
