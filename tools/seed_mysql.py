#!/usr/bin/env python3
"""Seed the MiniCRisk MySQL database with synthetic data.

Scale (default --scale 1.0):  20,000 companies, 200,000 credit requests, 100,000 documents,
200,000 scoring results (~1.2 KB JSON each) and 300,000 audit rows  -> roughly 0.5 GB.
Use --scale 0.05 for a quick smoke test, --scale 4 for ~2 GB.

The password is read from the MYSQL_PASSWORD environment variable (never from the command line).

  export MYSQL_PASSWORD='...'
  python3 tools/seed_mysql.py --host 10.0.1.20 --user admin --scale 1
"""
import argparse
import json
import os
import random
import sys
import time
import uuid
from datetime import datetime, timedelta, timezone

import pymysql

WORDS = ["Andes", "Bravo", "Cobre", "Delta", "Estrella", "Faro", "Granito", "Horizonte", "Isla", "Jade",
         "Kilo", "Lago", "Montana", "Norte", "Oasis", "Pacifico", "Quartz", "Roble", "Sierra", "Tundra"]
SUFFIX = ["S.A. de C.V.", "S. de R.L.", "SpA", "Ltda.", "Holdings", "Logistica", "Comercial", "Servicios"]
ACTORS = ["system", "api", "analyst1", "analyst2", "batch", "backoffice"]
ACTIONS = ["LOGIN", "VIEW", "EXPORT", "STATUS_CHANGE", "DOC_UPLOAD", "SCORE_REQUEST"]
SEGMENTS = ["A", "B", "C", "D"]
STATUSES = ["NEW", "SCORING", "SCORED", "APPROVED", "REJECTED"]
BATCH = 2000


def rand_dt(rng, days_back):
    return datetime.now(timezone.utc).replace(tzinfo=None) - timedelta(seconds=rng.randint(0, days_back * 86400))


def batches(total):
    start = 0
    while start < total:
        end = min(start + BATCH, total)
        yield start, end
        start = end


def progress(label, done, total, t0):
    print("  %-17s %9d / %-9d  %.0fs" % (label, done, total, time.time() - t0), flush=True)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", required=True)
    ap.add_argument("--port", type=int, default=3306)
    ap.add_argument("--user", required=True)
    ap.add_argument("--db", default="minicrisk")
    ap.add_argument("--scale", type=float, default=1.0)
    ap.add_argument("--seed", type=int, default=42, help="random seed (same seed = same data)")
    args = ap.parse_args()

    password = os.environ.get("MYSQL_PASSWORD")
    if not password:
        sys.exit("MYSQL_PASSWORD is not set")

    n_companies = max(10, int(20000 * args.scale))
    n_requests = max(50, int(200000 * args.scale))
    n_documents = max(20, int(100000 * args.scale))
    n_scores = n_requests
    n_audit = max(50, int(300000 * args.scale))

    rng = random.Random(args.seed)
    conn = pymysql.connect(host=args.host, port=args.port, user=args.user, password=password,
                           database=args.db, charset="utf8mb4", autocommit=False)
    cur = conn.cursor()
    cur.execute("SELECT COUNT(*) FROM companies")
    if cur.fetchone()[0] > 0:
        sys.exit("companies is not empty: refusing to seed twice (truncate the tables first)")

    t0 = time.time()
    print("Seeding %s on %s ..." % (args.db, args.host))

    # Each table is loaded in its own transactions. Triggers are not created yet (db/02-routines.sql comes after).
    cur.execute("SET FOREIGN_KEY_CHECKS = 1")

    for s, e in batches(n_companies):
        rows = []
        for i in range(s, e):
            name = "%s %s %s" % (rng.choice(WORDS), rng.choice(WORDS), rng.choice(SUFFIX))
            rows.append(("TX%010d" % (i + 1), name, rng.choice(["MX", "MX", "MX", "CL", "CO"]),
                         rng.randint(1985, 2023), rng.choice(SEGMENTS)))
        cur.executemany(
            "INSERT INTO companies (tax_id, legal_name, country, founded_year, risk_segment) VALUES (%s,%s,%s,%s,%s)",
            rows)
        conn.commit()
    progress("companies", n_companies, n_companies, t0)

    for s, e in batches(n_requests):
        rows = []
        for _ in range(s, e):
            created = rand_dt(rng, 900)
            rows.append((rng.randint(1, n_companies), round(rng.uniform(5000, 5000000), 2),
                         rng.choice(["MXN", "MXN", "MXN", "USD"]), rng.choice(STATUSES),
                         round(rng.uniform(0, 100), 3), "r-toy-1.0",
                         "synthetic request %s" % uuid.UUID(int=rng.getrandbits(128)).hex[:12], created))
        cur.executemany(
            "INSERT INTO credit_requests (company_id, amount, currency, status, score, model_version, notes, created_at) "
            "VALUES (%s,%s,%s,%s,%s,%s,%s,%s)", rows)
        conn.commit()
    progress("credit_requests", n_requests, n_requests, t0)

    for s, e in batches(n_documents):
        rows = []
        for _ in range(s, e):
            req = rng.randint(1, n_requests)
            name = "doc-%s.pdf" % uuid.UUID(int=rng.getrandbits(128)).hex[:10]
            rows.append((req, "%d/%s" % (req, name), name, "application/pdf", rng.randint(20000, 4000000)))
        cur.executemany(
            "INSERT INTO documents (credit_request_id, blob_path, file_name, content_type, size_bytes) "
            "VALUES (%s,%s,%s,%s,%s)", rows)
        conn.commit()
    progress("documents", n_documents, n_documents, t0)

    for s, e in batches(n_scores):
        rows = []
        for i in range(s, e):
            payload = json.dumps({
                "ModelVersion": "r-toy-1.0",
                "Score": round(rng.uniform(0, 100), 3),
                "Features": {"f%02d" % k: round(rng.random(), 6) for k in range(60)},
            })
            rows.append((str(uuid.UUID(int=rng.getrandbits(128))), i + 1, payload))
        cur.executemany(
            "INSERT INTO scoring_results (job_id, credit_request_id, raw_payload) VALUES (%s,%s,%s)", rows)
        conn.commit()
    progress("scoring_results", n_scores, n_scores, t0)

    for s, e in batches(n_audit):
        rows = []
        for _ in range(s, e):
            rows.append((rand_dt(rng, 900), rng.choice(ACTORS), rng.choice(ACTIONS),
                         "synthetic audit event %d" % rng.randint(1, 10 ** 9)))
        cur.executemany("INSERT INTO audit_log (event_time, actor, action, detail) VALUES (%s,%s,%s,%s)", rows)
        conn.commit()
    progress("audit_log", n_audit, n_audit, t0)

    cur.execute("SELECT table_name, ROUND((data_length + index_length)/1024/1024, 1) "
                "FROM information_schema.tables WHERE table_schema = %s AND table_type = 'BASE TABLE' ORDER BY 1",
                (args.db,))
    print("Done in %.0fs. Table sizes (MB):" % (time.time() - t0))
    for name, mb in cur.fetchall():
        print("  %-18s %s" % (name, mb))
    conn.close()


if __name__ == "__main__":
    main()
