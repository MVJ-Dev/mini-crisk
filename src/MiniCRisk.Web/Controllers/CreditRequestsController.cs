using System;
using System.Threading.Tasks;
using System.Web.Http;
using MiniCRisk.Web.Models;
using MiniCRisk.Web.Services;

namespace MiniCRisk.Web.Controllers
{
    [RoutePrefix("api/credit-requests")]
    public class CreditRequestsController : ApiController
    {
        /// <summary>Creates a credit request and enqueues the scoring job.</summary>
        [HttpPost, Route("")]
        public async Task<IHttpActionResult> Create([FromBody] CreateCreditRequestDto dto)
        {
            if (dto == null || dto.CompanyId <= 0 || dto.Amount <= 0)
            {
                return BadRequest("companyId and amount (> 0) are required");
            }

            var id = await ServiceRegistry.Repository.CreateAsync(dto);

            await ServiceRegistry.Queue.EnqueueAsync(new JobMessage
            {
                JobId = Guid.NewGuid(),
                Type = "SCORE",
                RequestId = id,
                EnqueuedUtc = DateTime.UtcNow
            });

            return Created("api/credit-requests/" + id, new { id });
        }

        public class IngestDto
        {
            public long RequestId { get; set; }
        }

        /// <summary>Called by the DocumentIngest function: (re)enqueues the scoring job for an existing request.</summary>
        [HttpPost, Route("ingest")]
        public async Task<IHttpActionResult> Ingest([FromBody] IngestDto dto)
        {
            if (dto == null || dto.RequestId <= 0)
            {
                return BadRequest("requestId is required");
            }

            var request = await ServiceRegistry.Repository.GetAsync(dto.RequestId);
            if (request == null)
            {
                return NotFound();
            }

            await ServiceRegistry.Queue.EnqueueAsync(new JobMessage
            {
                JobId = Guid.NewGuid(),
                Type = "SCORE",
                RequestId = dto.RequestId,
                EnqueuedUtc = DateTime.UtcNow
            });

            return Ok(new { queued = true, requestId = dto.RequestId });
        }

        [HttpGet, Route("{id:long}")]
        public async Task<IHttpActionResult> Get(long id)
        {
            var request = await ServiceRegistry.Repository.GetAsync(id);
            if (request == null)
            {
                return NotFound();
            }
            return Ok(request);
        }

        [HttpGet, Route("")]
        public async Task<IHttpActionResult> ListOpen(int limit = 50)
        {
            if (limit < 1 || limit > 500)
            {
                limit = 50;
            }
            return Ok(await ServiceRegistry.Repository.ListOpenAsync(limit));
        }
    }
}
