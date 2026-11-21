-- MiniCRisk MySQL 8.0 schema (source database of the migration exercise).
-- Apply with:  mysql -h <host> -u <admin> -p < db/01-schema.sql   and then   db/02-routines.sql
--
-- Seeded migration traps (on purpose, they exist in real databases):
--   * audit_log has NO primary key            -> DMS/CDC cannot reliably replay UPDATE/DELETE on it
--   * scoring_results.raw_payload is MEDIUMTEXT (LOB) -> DMS LOB mode (limited vs full) and size limits
--   * foreign keys                            -> load order / disable FK checks during full load
--   * view, trigger and stored procedure      -> NOT migrated by DMS; must be recreated on the target
--   * utf8mb4 + DATETIME(3)                   -> charset / precision must survive the migration unchanged

CREATE DATABASE IF NOT EXISTS minicrisk CHARACTER SET utf8mb4;
USE minicrisk;

CREATE TABLE IF NOT EXISTS companies (
  id            BIGINT NOT NULL AUTO_INCREMENT,
  tax_id        VARCHAR(20)  NOT NULL,
  legal_name    VARCHAR(200) NOT NULL,
  country       CHAR(2)      NOT NULL,
  founded_year  SMALLINT     NULL,
  risk_segment  ENUM('A','B','C','D') NOT NULL DEFAULT 'C',
  created_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at    DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_companies_tax_id (tax_id),
  KEY idx_companies_segment (risk_segment)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS credit_requests (
  id             BIGINT NOT NULL AUTO_INCREMENT,
  company_id     BIGINT NOT NULL,
  amount         DECIMAL(14,2) NOT NULL,
  currency       CHAR(3) NOT NULL DEFAULT 'MXN',
  status         ENUM('NEW','SCORING','SCORED','APPROVED','REJECTED') NOT NULL DEFAULT 'NEW',
  score          DECIMAL(6,3) NULL,
  model_version  VARCHAR(20)  NULL,
  notes          TEXT NULL,
  created_at     DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  updated_at     DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3) ON UPDATE CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_credit_requests_company (company_id),
  KEY idx_credit_requests_status_created (status, created_at),
  CONSTRAINT fk_credit_requests_company FOREIGN KEY (company_id) REFERENCES companies (id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS documents (
  id                 BIGINT NOT NULL AUTO_INCREMENT,
  credit_request_id  BIGINT NOT NULL,
  blob_path          VARCHAR(400) NOT NULL,
  file_name          VARCHAR(255) NOT NULL,
  content_type       VARCHAR(100) NOT NULL,
  size_bytes         BIGINT NOT NULL,
  uploaded_at        DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  KEY idx_documents_request (credit_request_id),
  CONSTRAINT fk_documents_request FOREIGN KEY (credit_request_id) REFERENCES credit_requests (id)
) ENGINE=InnoDB;

CREATE TABLE IF NOT EXISTS scoring_results (
  id                 BIGINT NOT NULL AUTO_INCREMENT,
  job_id             CHAR(36) NOT NULL,
  credit_request_id  BIGINT NOT NULL,
  raw_payload        MEDIUMTEXT NOT NULL,
  created_at         DATETIME(3) NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  PRIMARY KEY (id),
  UNIQUE KEY uq_scoring_results_job (job_id),
  KEY idx_scoring_results_request (credit_request_id),
  CONSTRAINT fk_scoring_results_request FOREIGN KEY (credit_request_id) REFERENCES credit_requests (id)
) ENGINE=InnoDB;

-- Intentionally WITHOUT primary key (legacy audit table)
CREATE TABLE IF NOT EXISTS audit_log (
  event_time  DATETIME(3)  NOT NULL DEFAULT CURRENT_TIMESTAMP(3),
  actor       VARCHAR(100) NOT NULL,
  action      VARCHAR(60)  NOT NULL,
  detail      TEXT NULL,
  KEY idx_audit_log_time (event_time)
) ENGINE=InnoDB;

CREATE OR REPLACE VIEW v_open_requests AS
  SELECT r.id, r.company_id, c.founded_year, r.amount, r.currency, r.status, r.score,
         r.model_version, r.notes, r.created_at
  FROM credit_requests r
  JOIN companies c ON c.id = r.company_id
  WHERE r.status IN ('NEW','SCORING','SCORED');
