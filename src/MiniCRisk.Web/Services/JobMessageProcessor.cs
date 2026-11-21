using System;
using System.Diagnostics;
using System.Threading.Tasks;
using MiniCRisk.Web.Models;
using Newtonsoft.Json;

namespace MiniCRisk.Web.Services
{
    /// <summary>Business handler for one queue message (scoring of a credit request).</summary>
    public class JobMessageProcessor
    {
        private readonly CreditRepository _repo;
        private readonly ScoringClient _scoring;

        public JobMessageProcessor(CreditRepository repo, ScoringClient scoring)
        {
            _repo = repo;
            _scoring = scoring;
        }

        public async Task ProcessAsync(JobMessage job)
        {
            var request = await _repo.GetAsync(job.RequestId).ConfigureAwait(false);
            if (request == null)
            {
                Trace.TraceWarning("Job {0}: request {1} not found, dropping", job.JobId, job.RequestId);
                return;
            }

            await _repo.SetStatusAsync(request.Id, "SCORING").ConfigureAwait(false);

            var age = request.CompanyFoundedYear.HasValue
                ? Math.Max(0, DateTime.UtcNow.Year - request.CompanyFoundedYear.Value)
                : 0;

            var result = await _scoring.ScoreAsync(new ScoreRequest
            {
                Amount = request.Amount,
                CompanyAgeYears = age,
                PriorDefaults = 0
            }).ConfigureAwait(false);

            await _repo.SaveScoreAsync(job.JobId, request.Id, result.Score, result.ModelVersion,
                JsonConvert.SerializeObject(result)).ConfigureAwait(false);

            Trace.TraceInformation("Job {0}: request {1} scored {2}", job.JobId, request.Id, result.Score);
        }
    }
}
