using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Web.Http;
using MiniCRisk.Web.Services;

namespace MiniCRisk.Web.Controllers
{
    /// <summary>Operational endpoints that exercise the platform dependencies one by one (useful for smoke tests after cutover).</summary>
    [RoutePrefix("api")]
    public class PlatformController : ApiController
    {
        /// <summary>Approximate size of the jobs queue.</summary>
        [HttpGet, Route("jobs/stats")]
        public async Task<IHttpActionResult> QueueStats()
        {
            var pending = await ServiceRegistry.Queue.GetApproximateCountAsync();
            return Ok(new { pending });
        }

        /// <summary>Signs a payload with the partner certificate (needs private-key access for the app pool identity).</summary>
        [HttpGet, Route("partner/ping")]
        public IHttpActionResult PartnerPing()
        {
            try
            {
                return Ok(PartnerRequestSigner.SignPing());
            }
            catch (CryptographicException ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    new { error = "private key not accessible", detail = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Content(HttpStatusCode.InternalServerError,
                    new { error = "certificate problem", detail = ex.Message });
            }
        }

        /// <summary>Checks a name against the compliance blacklist (File Share today, S3 after migration).</summary>
        [HttpGet, Route("reference/blacklist")]
        public async Task<IHttpActionResult> Blacklist(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return BadRequest("name is required");
            }
            var hit = await ServiceRegistry.Reference.IsBlacklistedAsync(name);
            return Ok(new { name, blacklisted = hit });
        }
    }
}
