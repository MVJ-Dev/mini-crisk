using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using MiniCRisk.Web.Models;
using MySql.Data.MySqlClient;

namespace MiniCRisk.Web.Services
{
    /// <summary>MySQL access. The connection string comes from the vault (secret "mysql-connection-string").</summary>
    public class CreditRepository
    {
        private readonly string _connectionString;

        public CreditRepository(VaultSecretProvider vault)
        {
            _connectionString = vault.GetSecret("mysql-connection-string");
        }

        private async Task<MySqlConnection> OpenAsync()
        {
            var conn = new MySqlConnection(_connectionString);
            await conn.OpenAsync().ConfigureAwait(false);
            return conn;
        }

        public async Task PingAsync()
        {
            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand("SELECT 1", conn))
            {
                await cmd.ExecuteScalarAsync().ConfigureAwait(false);
            }
        }

        public async Task<long> CreateAsync(CreateCreditRequestDto dto)
        {
            const string sql =
                "INSERT INTO credit_requests (company_id, amount, currency, status, notes) " +
                "VALUES (@company, @amount, @currency, 'NEW', @notes); SELECT LAST_INSERT_ID();";

            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@company", dto.CompanyId);
                cmd.Parameters.AddWithValue("@amount", dto.Amount);
                cmd.Parameters.AddWithValue("@currency", string.IsNullOrEmpty(dto.Currency) ? "MXN" : dto.Currency);
                cmd.Parameters.AddWithValue("@notes", (object)dto.Notes ?? DBNull.Value);
                var id = await cmd.ExecuteScalarAsync().ConfigureAwait(false);
                return Convert.ToInt64(id);
            }
        }

        public async Task<CreditRequest> GetAsync(long id)
        {
            const string sql =
                "SELECT r.id, r.company_id, c.founded_year, r.amount, r.currency, r.status, r.score, " +
                "       r.model_version, r.notes, r.created_at " +
                "FROM credit_requests r JOIN companies c ON c.id = r.company_id WHERE r.id = @id";

            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", id);
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    return await reader.ReadAsync().ConfigureAwait(false) ? Map(reader) : null;
                }
            }
        }

        public async Task<List<CreditRequest>> ListOpenAsync(int limit)
        {
            const string sql =
                "SELECT id, company_id, founded_year, amount, currency, status, score, model_version, notes, created_at " +
                "FROM v_open_requests ORDER BY id DESC LIMIT @limit";

            var list = new List<CreditRequest>();
            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@limit", limit);
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        list.Add(Map(reader));
                    }
                }
            }
            return list;
        }

        public async Task SetStatusAsync(long id, string status)
        {
            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand("UPDATE credit_requests SET status = @status WHERE id = @id", conn))
            {
                cmd.Parameters.AddWithValue("@status", status);
                cmd.Parameters.AddWithValue("@id", id);
                await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Idempotent: scoring_results.job_id is UNIQUE, so a redelivered queue message does not duplicate rows.
        /// </summary>
        public async Task SaveScoreAsync(Guid jobId, long requestId, decimal score, string modelVersion, string rawPayload)
        {
            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var tx = conn.BeginTransaction())
            {
                using (var insert = new MySqlCommand(
                    "INSERT IGNORE INTO scoring_results (job_id, credit_request_id, raw_payload) VALUES (@job, @req, @payload)",
                    conn, tx))
                {
                    insert.Parameters.AddWithValue("@job", jobId.ToString());
                    insert.Parameters.AddWithValue("@req", requestId);
                    insert.Parameters.AddWithValue("@payload", rawPayload);
                    await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                using (var update = new MySqlCommand(
                    "UPDATE credit_requests SET score = @score, model_version = @model, status = 'SCORED' WHERE id = @id",
                    conn, tx))
                {
                    update.Parameters.AddWithValue("@score", score);
                    update.Parameters.AddWithValue("@model", modelVersion);
                    update.Parameters.AddWithValue("@id", requestId);
                    await update.ExecuteNonQueryAsync().ConfigureAwait(false);
                }

                tx.Commit();
            }
        }

        public async Task<long> AddDocumentAsync(long requestId, string blobPath, string fileName, string contentType, long sizeBytes)
        {
            const string sql =
                "INSERT INTO documents (credit_request_id, blob_path, file_name, content_type, size_bytes) " +
                "VALUES (@req, @path, @name, @type, @size); SELECT LAST_INSERT_ID();";

            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@req", requestId);
                cmd.Parameters.AddWithValue("@path", blobPath);
                cmd.Parameters.AddWithValue("@name", fileName);
                cmd.Parameters.AddWithValue("@type", contentType);
                cmd.Parameters.AddWithValue("@size", sizeBytes);
                return Convert.ToInt64(await cmd.ExecuteScalarAsync().ConfigureAwait(false));
            }
        }

        public async Task<DocumentRecord> GetDocumentAsync(long documentId)
        {
            const string sql =
                "SELECT id, credit_request_id, blob_path, file_name, content_type, size_bytes FROM documents WHERE id = @id";

            using (var conn = await OpenAsync().ConfigureAwait(false))
            using (var cmd = new MySqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@id", documentId);
                using (var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false))
                {
                    if (!await reader.ReadAsync().ConfigureAwait(false))
                    {
                        return null;
                    }
                    return new DocumentRecord
                    {
                        Id = Convert.ToInt64(reader["id"]),
                        CreditRequestId = Convert.ToInt64(reader["credit_request_id"]),
                        BlobPath = Convert.ToString(reader["blob_path"]),
                        FileName = Convert.ToString(reader["file_name"]),
                        ContentType = Convert.ToString(reader["content_type"]),
                        SizeBytes = Convert.ToInt64(reader["size_bytes"])
                    };
                }
            }
        }

        private static CreditRequest Map(DbDataReader r)
        {
            return new CreditRequest
            {
                Id = Convert.ToInt64(r["id"]),
                CompanyId = Convert.ToInt64(r["company_id"]),
                CompanyFoundedYear = r["founded_year"] == DBNull.Value ? (int?)null : Convert.ToInt32(r["founded_year"]),
                Amount = Convert.ToDecimal(r["amount"]),
                Currency = Convert.ToString(r["currency"]),
                Status = Convert.ToString(r["status"]),
                Score = r["score"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(r["score"]),
                ModelVersion = r["model_version"] == DBNull.Value ? null : Convert.ToString(r["model_version"]),
                Notes = r["notes"] == DBNull.Value ? null : Convert.ToString(r["notes"]),
                CreatedUtc = DateTime.SpecifyKind(Convert.ToDateTime(r["created_at"]), DateTimeKind.Utc)
            };
        }
    }
}
