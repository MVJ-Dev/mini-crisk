#!/usr/bin/env python3
"""Generate continuous changes on the SOURCE database while DMS is replicating (CDC rehearsal).

Mix of operations (approximate): 55% INSERT credit_request, 30% UPDATE status/score, 8% UPDATE company,
5% INSERT audit_log, 2% DELETE old audit_log rows (table without PK on purpose).
Optionally a periodic BURST: one large transaction (stresses CDC latency / apply).

Stop it with Ctrl+C; it prints counters every 10 seconds and a final summary.
Use it to FREEZE writes at cutover time: stop this script (and the app), wait for CDC latency = 0, then validate.

  export MYSQL_PASSWORD='...'
  python3 tools/cdc_traffic.py --host 10.0.1.20 --user admin --rate 20 --burst-every 120 --burst-size 5000
"""
import argparse
import os
import random
import signal
import sys
import time

import pymysql

STOP = False


def handle_sigint(signum, frame):
    global STOP
    STOP = True


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", required=True)
    ap.add_argument("--port", type=int, default=3306)
    ap.add_argument("--user", required=True)
    ap.add_argument("--db", default="minicrisk")
    ap.add_argument("--rate", type=float, default=10.0, help="operations per second (approx.)")
    ap.add_argument("--minutes", type=float, default=0, help="stop after N minutes (0 = until Ctrl+C)")
    ap.add_argument("--burst-every", type=int, default=0, help="seconds between bursts (0 = no bursts)")
    ap.add_argument("--burst-size", type=int, default=5000, help="rows inserted per burst, in ONE transaction")
    args = ap.parse_args()

    password = os.environ.get("MYSQL_PASSWORD")
    if not password:
        sys.exit("MYSQL_PASSWORD is not set")

    signal.signal(signal.SIGINT, handle_sigint)
    signal.signal(signal.SIGTERM, handle_sigint)

    conn = pymysql.connect(host=args.host, port=args.port, user=args.user, password=password,
                           database=args.db, charset="utf8mb4", autocommit=True)
    cur = conn.cursor()
    cur.execute("SELECT COALESCE(MAX(id), 0) FROM companies")
    max_company = cur.fetchone()[0]
    cur.execute("SELECT COALESCE(MAX(id), 0) FROM credit_requests")
    max_request = cur.fetchone()[0]
    if max_company == 0 or max_request == 0:
        sys.exit("database is empty: run tools/seed_mysql.py first")

    rng = random.Random()
    counters = {"insert_request": 0, "update_request": 0, "update_company": 0,
                "insert_audit": 0, "delete_audit": 0, "burst_rows": 0, "errors": 0}
    t0 = time.time()
    last_report = t0
    last_burst = t0
    interval = 1.0 / args.rate if args.rate > 0 else 0

    print("Generating changes on %s/%s at ~%.1f ops/s. Ctrl+C to stop." % (args.host, args.db, args.rate))

    while not STOP:
        now = time.time()
        if args.minutes and (now - t0) > args.minutes * 60:
            break

        try:
            if args.burst_every and (now - last_burst) >= args.burst_every:
                conn.begin()
                rows = [(rng.randint(1, max_company), round(rng.uniform(5000, 5000000), 2), "MXN", "NEW",
                         "burst row") for _ in range(args.burst_size)]
                cur.executemany(
                    "INSERT INTO credit_requests (company_id, amount, currency, status, notes) VALUES (%s,%s,%s,%s,%s)",
                    rows)
                conn.commit()
                counters["burst_rows"] += args.burst_size
                max_request += args.burst_size
                last_burst = time.time()

            roll = rng.random()
            if roll < 0.55:
                cur.execute(
                    "INSERT INTO credit_requests (company_id, amount, currency, status, notes) "
                    "VALUES (%s,%s,'MXN','NEW','cdc traffic')",
                    (rng.randint(1, max_company), round(rng.uniform(5000, 5000000), 2)))
                max_request = max(max_request, cur.lastrowid)
                counters["insert_request"] += 1
            elif roll < 0.85:
                cur.execute("UPDATE credit_requests SET status = %s, score = %s, model_version = 'r-toy-1.0' WHERE id = %s",
                            (rng.choice(["SCORING", "SCORED", "APPROVED", "REJECTED"]),
                             round(rng.uniform(0, 100), 3), rng.randint(1, max_request)))
                counters["update_request"] += 1
            elif roll < 0.93:
                cur.execute("UPDATE companies SET risk_segment = %s WHERE id = %s",
                            (rng.choice(["A", "B", "C", "D"]), rng.randint(1, max_company)))
                counters["update_company"] += 1
            elif roll < 0.98:
                cur.execute("INSERT INTO audit_log (actor, action, detail) VALUES ('cdc-traffic', 'SCORE_REQUEST', %s)",
                            ("event %d" % rng.randint(1, 10 ** 9),))
                counters["insert_audit"] += 1
            else:
                cur.execute("DELETE FROM audit_log WHERE actor <> 'cdc-traffic' ORDER BY event_time LIMIT 3")
                counters["delete_audit"] += 1
        except pymysql.MySQLError as exc:
            counters["errors"] += 1
            print("  error: %s" % exc, flush=True)
            time.sleep(1)

        if interval:
            time.sleep(interval)

        if time.time() - last_report >= 10:
            print("  [%4.0fs] %s" % (time.time() - t0, counters), flush=True)
            last_report = time.time()

    print("Stopped. Totals: %s" % counters)
    conn.close()


if __name__ == "__main__":
    main()
