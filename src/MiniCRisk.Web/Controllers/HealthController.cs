using System;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using MiniCRisk.Web.Services;

namespace MiniCRisk.Web.Controllers
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        /// <summary>Shallow check: no dependencies. Use this one for load balancer health checks.</summary>
        [HttpGet, Route("")]
        public IHttpActionResult Get()
        {
            return Ok(new { status = "ok", utc = DateTime.UtcNow, machine = Environment.MachineName });
        }

        /// <summary>Deep check: vault + database + queue.</summary>
        [HttpGet, Route("deep")]
        public async Task<IHttpActionResult> Deep()
        {
            try
            {
                ServiceRegistry.Vault.GetSecret("mysql-connection-string");
                await ServiceRegistry.Repository.PingAsync();
                var pending = await ServiceRegistry.Queue.GetApproximateCountAsync();
                return Ok(new { status = "ok", database = "ok", queuePending = pending, utc = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                return Content(HttpStatusCode.ServiceUnavailable,
                    new { status = "degraded", error = ex.GetType().Name, message = ex.Message });
            }
        }
    }
}
