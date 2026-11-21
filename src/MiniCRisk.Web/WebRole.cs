#if AZURE_CLOUD_SERVICE
// Azure Cloud Services (extended support) entry point.
// Compiled only when the Azure SDK is installed. This class has NO equivalent on EC2/IIS:
// anything useful in OnStart() must move to Global.asax (Application_Start).
using System.Linq;
using System.Net;
using Microsoft.WindowsAzure.ServiceRuntime;

namespace MiniCRisk.Web
{
    public class WebRole : RoleEntryPoint
    {
        public override bool OnStart()
        {
            // Raise the outbound connection limit (default is 2 per host)
            ServicePointManager.DefaultConnectionLimit = 48;

            // Recycle the role when a configuration SETTING changes in the .cscfg
            RoleEnvironment.Changing += (sender, e) =>
            {
                if (e.Changes.OfType<RoleEnvironmentConfigurationSettingChange>().Any())
                {
                    e.Cancel = true;
                }
            };

            return base.OnStart();
        }
    }
}
#endif
