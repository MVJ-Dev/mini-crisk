using System;

namespace MiniCRisk.Web.Models
{
    public class CreateCreditRequestDto
    {
        public long CompanyId { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Notes { get; set; }
    }

    public class CreditRequest
    {
        public long Id { get; set; }
        public long CompanyId { get; set; }
        public int? CompanyFoundedYear { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public string Status { get; set; }
        public decimal? Score { get; set; }
        public string ModelVersion { get; set; }
        public string Notes { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public class DocumentRecord
    {
        public long Id { get; set; }
        public long CreditRequestId { get; set; }
        public string BlobPath { get; set; }
        public string FileName { get; set; }
        public string ContentType { get; set; }
        public long SizeBytes { get; set; }
    }

    /// <summary>Message format of the jobs queue (JSON). Keep it stable across the migration.</summary>
    public class JobMessage
    {
        public Guid JobId { get; set; }
        public string Type { get; set; }          // "SCORE"
        public long RequestId { get; set; }
        public string DocumentPath { get; set; }  // optional
        public DateTime EnqueuedUtc { get; set; }
    }

    public class ScoreRequest
    {
        public decimal Amount { get; set; }
        public int CompanyAgeYears { get; set; }
        public int PriorDefaults { get; set; }
    }

    public class ScoreResult
    {
        public decimal Score { get; set; }
        public string ModelVersion { get; set; }
    }
}
