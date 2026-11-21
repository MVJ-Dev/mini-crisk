using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MiniCRisk.Web.Config;
using MiniCRisk.Web.Models;
using Newtonsoft.Json;

namespace MiniCRisk.Web.Services
{
    /// <summary>
    /// Calls the R scoring host (Plumber API). On Azure the R runtime is installed by startup.cmd;
    /// on AWS it must come from a custom AMI / container image.
    /// </summary>
    public class ScoringClient
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        private readonly string _endpoint;
        private readonly string _apiKey;

        public ScoringClient(VaultSecretProvider vault)
        {
            _endpoint = AppConfig.Get("Scoring_Endpoint", "http://localhost:8000/score");
            _apiKey = vault.GetSecret("scoring-api-key");
        }

        public async Task<ScoreResult> ScoreAsync(ScoreRequest request)
        {
            var body = new StringContent(JsonConvert.SerializeObject(request), Encoding.UTF8, "application/json");
            using (var msg = new HttpRequestMessage(HttpMethod.Post, _endpoint) { Content = body })
            {
                msg.Headers.Add("X-Api-Key", _apiKey);
                using (var response = await Http.SendAsync(msg).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    return JsonConvert.DeserializeObject<ScoreResult>(json);
                }
            }
        }
    }
}
