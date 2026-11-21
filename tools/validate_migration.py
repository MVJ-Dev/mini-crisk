#!/usr/bin/env python3
"""Compare SOURCE and TARGET databases table by table: row count + content checksum.

Checksum per table = SUM(CRC32(CONCAT_WS('#', every column cast to text))). The SUM makes it independent of row
order, so it also works for tables without a primary key (audit_log).

Run it with writes FROZEN (stop the app and tools/cdc_traffic.py, wait until CDC latency is 0):
while changes are still flowing, differences are expected.

  export SRC_PASSWORD='...'  DST_PASSWORD='...'
  python3 tools/validate_migration.py --src-host 10.0.1.20 --dst-host minicrisk.xxxx.rds.amazonaws.com --user admin

Exit code 0 = everything matches, 1 = at least one difference.
It only reads (SELECT); it never modifies either database.
"""
import argparse
import os
import sys

import pymysql


def connect(host, port, user, password, db):
    return pymysql.connect(host=host, port=port, user=user, password=password, database=db,
                           charset="utf8mb4", autocommit=True, read_timeout=3600)


def list_tables(cur, db):
    cur.execute("SELECT table_name FROM information_schema.tables "
                "WHERE table_schema = %s AND table_type = 'BASE TABLE' ORDER BY table_name", (db,))
    return [r[0] for r in cur.fetchall()]


def list_columns(cur, db, table):
    cur.execute("SELECT column_name FROM information_schema.columns "
                "WHERE table_schema = %s AND table_name = %s ORDER BY ordinal_position", (db, table))
    return [r[0] for r in cur.fetchall()]


def table_fingerprint(cur, table, columns):
    parts = ", ".join("COALESCE(CAST(`%s` AS CHAR), '<NULL>')" % c for c in columns)
    cur.execute("SELECT COUNT(*), COALESCE(SUM(CRC32(CONCAT_WS('#', %s))), 0) FROM `%s`" % (parts, table))
    count, checksum = cur.fetchone()
    return int(count), str(checksum)


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--src-host", required=True)
    ap.add_argument("--dst-host", required=True)
    ap.add_argument("--port", type=int, default=3306)
    ap.add_argument("--user", required=True)
    ap.add_argument("--db", default="minicrisk")
    args = ap.parse_args()

    src_pw = os.environ.get("SRC_PASSWORD")
    dst_pw = os.environ.get("DST_PASSWORD")
    if not src_pw or not dst_pw:
        sys.exit("SRC_PASSWORD and DST_PASSWORD must be set")

    src = connect(args.src_host, args.port, args.user, src_pw, args.db).cursor()
    dst = connect(args.dst_host, args.port, args.user, dst_pw, args.db).cursor()

    src_tables = list_tables(src, args.db)
    dst_tables = list_tables(dst, args.db)
    failures = 0

    missing = sorted(set(src_tables) - set(dst_tables))
    extra = sorted(set(dst_tables) - set(src_tables))
    if missing:
        print("MISSING on target: %s" % ", ".join(missing))
        failures += len(missing)
    if extra:
        print("EXTRA on target:   %s" % ", ".join(extra))

    print("%-18s %12s %12s  %-6s %-9s" % ("table", "src_rows", "dst_rows", "count", "checksum"))
    for table in [t for t in src_tables if t in dst_tables]:
        cols = list_columns(src, args.db, table)
        dst_cols = list_columns(dst, args.db, table)
        if cols != dst_cols:
            print("%-18s column list differs (src=%s dst=%s)" % (table, cols, dst_cols))
            failures += 1
            continue
        s_count, s_sum = table_fingerprint(src, table, cols)
        d_count, d_sum = table_fingerprint(dst, table, cols)
        count_ok = s_count == d_count
        sum_ok = s_sum == d_sum
        if not (count_ok and sum_ok):
            failures += 1
        print("%-18s %12d %12d  %-6s %-9s" % (table, s_count, d_count, "OK" if count_ok else "DIFF",
                                              "OK" if sum_ok else "DIFF"))

    # Objects that DMS does not migrate: report presence so they are not forgotten
    for label, query in (("views", "SELECT COUNT(*) FROM information_schema.views WHERE table_schema = %s"),
                         ("routines", "SELECT COUNT(*) FROM information_schema.routines WHERE routine_schema = %s"),
                         ("triggers", "SELECT COUNT(*) FROM information_schema.triggers WHERE trigger_schema = %s")):
        src.execute(query, (args.db,))
        dst.execute(query, (args.db,))
        s, d = src.fetchone()[0], dst.fetchone()[0]
        flag = "OK" if s == d else "DIFF (recreate on target)"
        if s != d:
            failures += 1
        print("%-18s %12d %12d  %s" % (label, s, d, flag))

    print("\nRESULT: %s" % ("ALL MATCH" if failures == 0 else "%d difference(s)" % failures))
    sys.exit(0 if failures == 0 else 1)


if __name__ == "__main__":
    main()
