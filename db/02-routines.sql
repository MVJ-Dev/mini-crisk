-- Stored procedure and trigger (not migrated by DMS: recreate them on the target AFTER the full load).
-- Apply with the mysql client (it understands DELIMITER):  mysql -h <host> -u <admin> -p minicrisk < db/02-routines.sql
--
-- Migration notes:
--   * Do NOT create the trigger on the target before the full load finishes: it would write extra audit_log rows.
--   * If you move these objects with a dump, DEFINER=user@host clauses can fail on RDS (no SUPER): review them.

USE minicrisk;

DROP PROCEDURE IF EXISTS sp_close_stale_requests;
DROP TRIGGER IF EXISTS trg_credit_requests_status_audit;

DELIMITER $$

CREATE PROCEDURE sp_close_stale_requests(IN p_days INT)
BEGIN
  UPDATE credit_requests
     SET status = 'REJECTED'
   WHERE status = 'NEW'
     AND created_at < (NOW(3) - INTERVAL p_days DAY);
END$$

CREATE TRIGGER trg_credit_requests_status_audit
AFTER UPDATE ON credit_requests
FOR EACH ROW
BEGIN
  IF NEW.status <> OLD.status THEN
    INSERT INTO audit_log (actor, action, detail)
    VALUES ('system', 'STATUS_CHANGE', CONCAT('request ', NEW.id, ': ', OLD.status, ' -> ', NEW.status));
  END IF;
END$$

DELIMITER ;
