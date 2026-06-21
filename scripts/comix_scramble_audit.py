#!/usr/bin/env python3
"""
comix_scramble_audit.py — End-to-end audit of gateway downloads for tile-scrambled
pages, in one script.

It does both halves of the job:
  1. QUERY  — pull every chapter Mangarr downloaded from a given gateway upstream
              source (default "comix") straight from the Mangarr SQLite DB, with
              manga name, manga id, chapter number, file path and grabbed date.
  2. SCAN   — open each CBZ and detect anti-scrape tile-scrambled pages.

The two stages used to be separate (export-source-downloads.sh + cbz_scramble_scan.py);
this merges them and writes a single CSV joining the download metadata to the scan
verdict.

----------------------------------------------------------------------------------
Source attribution (stage 1)
----------------------------------------------------------------------------------
The gateway's upstream source isn't a DB column; it's the prefix on
ChapterHistory.ReleaseGuid (e.g. "comix:...", "mangadot:..."). A "download" is an
Imported event (EventType=3) whose chapter's most-recent preceding Grabbed event
(EventType=1) came from that source — anchoring on the nearest grab-before-import
attributes re-grabs correctly and ignores chapters later upgraded from elsewhere.

The DB lives on the dev stack; we reach it through an ephemeral sqlite container over
a docker context (default), or read a local copy with --db-local, or skip the query
entirely with --from-csv.

----------------------------------------------------------------------------------
Scramble detection (stage 2)
----------------------------------------------------------------------------------
A tile-permuted page is a permutation of equal-sized tiles, so at the true N×N grid
it has hard pixel discontinuities along EVERY internal seam line of at least one axis.
For the best axis/grid (grids 5..10, >=4 internal lines — at G=4 a centred bubble
fools all 3 quarter-lines), we compute three features and require ALL THREE to flag:

  * RATIO  >= --threshold (default 3.0): MEDIAN of the per-seam |delta| ratios
    (seam delta / image baseline delta). Captures the regular-grid STRUCTURE; the
    median (not mean) ignores a lone sharp edge.

  * SEAMABS >= --seam-floor (default 20): MEDIAN ABSOLUTE pixel jump (0-255 gray) at
    those seams. Kills near-uniform spacer pages (all black/white), where the ratio
    explodes from a ÷~0 baseline but the real jump is ~0 (seamabs ~0-1.5).

  * FMAIN  >= --fmain-floor (default 0.7): FRACTION of that axis's internal seam
    lines that actually break (delta > 2× baseline). A true permutation breaks at
    EVERY line (1.0); coherent content pages (panels, credits, dialogue) only break
    at a couple (<=0.6) even when their ratio is high from large flat regions. This
    is what distinguishes a real low-contrast pastel scramble (fmain 1.0, seamabs 33)
    from a flat content false positive (fmain 0.5, seamabs 41).

Empirically these three conditions give clean separation, validated against dozens of
visually-confirmed real scrambles and false positives (blank spacers, sky pages,
credits pages, flat dialogue panels, pastel/white-heavy scrambles).

----------------------------------------------------------------------------------
Usage
----------------------------------------------------------------------------------
  # full audit of comix downloads (query dev-stack DB, scan local mount):
  comix_scramble_audit.py --out comix-audit.csv

  # different source:
  comix_scramble_audit.py --source mangafire --out mangafire-audit.csv

  # reuse a previously-exported download list (skip the DB query):
  comix_scramble_audit.py --from-csv comix-downloads.csv --out audit.csv

  # just the download list, no scanning:
  comix_scramble_audit.py --no-scan --out comix-downloads.csv

Options
  --source NAME        gateway upstream source prefix (default: comix)
  --from-csv FILE      skip the DB query; read rows from this CSV (needs FilePath col)
  --no-scan            only produce the download list (stage 1)
  --path-map OLD=NEW   rewrite container path -> local mount (repeatable; default
                       /data/media/=/Users/<user>/Remote/Manga/)
  --threshold FLOAT    ratio cutoff (default 3.0)
  --seam-floor FLOAT   absolute median seam-jump cutoff in gray levels (default 48)
  --grids A B          grid range to probe (default 5 10)
  --workers N          parallel scan processes (default: CPU count)
  --out FILE           output CSV (default: comix-scramble-audit.csv)
  --verbose            print every CBZ verdict as it completes
  DB access (stage 1, ignored with --from-csv):
  --docker-context CTX docker context that can see the config dir (default: mediaserver)
  --config-dir DIR     host path of Mangarr /config holding the DB (default: /opt/mangarr)
  --db-name NAME       database filename (default: mangarr.db)
  --sqlite-image IMG   ephemeral sqlite image (default: keinos/sqlite3)
  --db-local PATH      read this local DB file directly instead of via docker
"""
import argparse
import csv
import io
import os
import subprocess
import sys
import zipfile
from concurrent.futures import ProcessPoolExecutor, as_completed

import numpy as np
from PIL import Image

Image.MAX_IMAGE_PIXELS = None
IMG_EXTS = (".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif")

# ---------------------------------------------------------------------------
# Stage 1 — query downloads for a source from the Mangarr DB
# ---------------------------------------------------------------------------
SQL_TEMPLATE = """
WITH imports AS (
  SELECT ChapterId, MangaId, Date AS importDate,
         json_extract(Data,'$.importedPath') AS path
  FROM ChapterHistory WHERE EventType = 3
),
attributed AS (
  SELECT i.*,
    (SELECT g.ReleaseGuid FROM ChapterHistory g
       WHERE g.EventType=1 AND g.ChapterId=i.ChapterId AND g.Date<=i.importDate
       ORDER BY g.Date DESC LIMIT 1) AS guid,
    (SELECT g.Date FROM ChapterHistory g
       WHERE g.EventType=1 AND g.ChapterId=i.ChapterId AND g.Date<=i.importDate
       ORDER BY g.Date DESC LIMIT 1) AS grabDate
  FROM imports i
)
SELECT m.Title AS MangaName, a.MangaId AS MangaId, c.ChapterNumber AS ChapterNumber,
       a.path AS FilePath, a.grabDate AS GrabbedDate
FROM attributed a
JOIN Manga m ON m.Id = a.MangaId
JOIN Chapters c ON c.Id = a.ChapterId
WHERE a.guid LIKE '{source}:%'
ORDER BY a.grabDate DESC;
"""


def query_downloads(args):
    if args.source and not args.source.replace("_", "").replace("-", "").replace(".", "").isalnum():
        sys.exit(f"error: invalid source name {args.source!r}")
    sql = SQL_TEMPLATE.format(source=args.source)
    if args.db_local:
        import sqlite3
        con = sqlite3.connect(f"file:{args.db_local}?mode=ro", uri=True)
        con.row_factory = sqlite3.Row
        try:
            rows = [dict(r) for r in con.execute(sql)]
        finally:
            con.close()
        return rows
    # via ephemeral sqlite container over a docker context
    cmd = ["docker", "--context", args.docker_context, "run", "--rm",
           "-v", f"{args.config_dir}:/db:ro", args.sqlite_image,
           "sqlite3", "-csv", "-header", f"/db/{args.db_name}", sql]
    print(f"Querying '{args.source}' downloads via docker context "
          f"'{args.docker_context}'...", file=sys.stderr)
    out = subprocess.run(cmd, capture_output=True, text=True)
    if out.returncode != 0:
        sys.exit(f"error: DB query failed:\n{out.stderr.strip()}")
    return list(csv.DictReader(io.StringIO(out.stdout)))


# ---------------------------------------------------------------------------
# Stage 2 — scramble detection
# ---------------------------------------------------------------------------
def page_metrics(data, grids):
    """Return (ratio, seam_abs, fmain, grid_desc) for one image's most grid-like axis.

    ratio   = median(seam delta)/baseline  -> grid STRUCTURE
    seam_abs= median absolute seam delta    -> MAGNITUDE (kills uniform pages)
    fmain   = fraction of seams > 2*baseline -> COMPLETENESS (kills content pages)
    """
    a = np.asarray(Image.open(io.BytesIO(data)).convert("L"), dtype=np.float32)
    H, W = a.shape
    if H < 16 or W < 16:
        return 0.0, 0.0, 0.0, ""
    coldiff = np.abs(a[:, 1:] - a[:, :-1]).mean(axis=0)
    rowdiff = np.abs(a[1:, :] - a[:-1, :]).mean(axis=1)
    base_c = float(coldiff.mean()) + 1e-6
    base_r = float(rowdiff.mean()) + 1e-6
    best_ratio, best_abs, best_frac, best_desc = 0.0, 0.0, 0.0, ""
    for G in grids:
        if G < 5:
            continue
        xs = [x for x in (round(W * c / G) - 1 for c in range(1, G)) if 0 <= x < len(coldiff)]
        ys = [y for y in (round(H * r / G) - 1 for r in range(1, G)) if 0 <= y < len(rowdiff)]
        if len(xs) >= 4:
            seam = coldiff[xs]
            ratio = float(np.median(seam)) / base_c
            if ratio > best_ratio:
                best_ratio = ratio
                best_abs = float(np.median(seam))
                best_frac = float((seam > 2 * base_c).mean())
                best_desc = f"C{G}"
        if len(ys) >= 4:
            seam = rowdiff[ys]
            ratio = float(np.median(seam)) / base_r
            if ratio > best_ratio:
                best_ratio = ratio
                best_abs = float(np.median(seam))
                best_frac = float((seam > 2 * base_r).mean())
                best_desc = f"R{G}"
    return best_ratio, best_abs, best_frac, best_desc


def scan_cbz(path, threshold, seam_floor, fmain_floor, grids):
    res = {"status": "OK", "pages": 0, "scrambled_count": 0,
           "scrambled_pages": "", "max_score": 0.0, "max_seamabs": 0.0, "detail": ""}
    if not path or not os.path.exists(path):
        res["status"] = "MISSING"
        return res
    try:
        with zipfile.ZipFile(path) as z:
            names = sorted(n for n in z.namelist()
                           if n.lower().endswith(IMG_EXTS) and not n.endswith("/"))
            res["pages"] = len(names)
            flagged, mscore, mabs = [], 0.0, 0.0
            for idx, name in enumerate(names, 1):
                try:
                    ratio, sabs, fmain, desc = page_metrics(z.read(name), grids)
                except Exception:
                    continue
                mscore = max(mscore, ratio)
                if ratio >= threshold and sabs >= seam_floor and fmain >= fmain_floor:
                    mabs = max(mabs, sabs)
                    flagged.append((idx, name, ratio, sabs, fmain, desc))
            res["max_score"] = round(mscore, 2)
            res["max_seamabs"] = round(mabs, 1)
            if flagged:
                res["status"] = "SCRAMBLED"
                res["scrambled_count"] = len(flagged)
                res["scrambled_pages"] = ",".join(str(i) for i, *_ in flagged)
                res["detail"] = "; ".join(
                    f"{n}(r={r:.1f},abs={s:.0f},f={f:.2f},{d})"
                    for _, n, r, s, f, d in flagged)
    except zipfile.BadZipFile:
        res["status"] = "BADZIP"
    except Exception as e:
        res["status"] = "ERROR"
        res["detail"] = str(e)
    return res


def remap(path, maps):
    for old, new in maps:
        if path and path.startswith(old):
            return new + path[len(old):]
    return path


def _worker(t):
    path, threshold, seam_floor, fmain_floor, grids = t
    return path, scan_cbz(path, threshold, seam_floor, fmain_floor, grids)


def main():
    ap = argparse.ArgumentParser(
        description="Audit gateway downloads from one source for tile-scrambled pages.")
    ap.add_argument("--source", default="comix")
    ap.add_argument("--from-csv")
    ap.add_argument("--no-scan", action="store_true")
    ap.add_argument("--path-map", action="append", default=[])
    ap.add_argument("--threshold", type=float, default=3.0)
    ap.add_argument("--seam-floor", type=float, default=20.0)
    ap.add_argument("--fmain-floor", type=float, default=0.7)
    ap.add_argument("--grids", nargs=2, type=int, default=[5, 10], metavar=("MIN", "MAX"))
    ap.add_argument("--workers", type=int, default=os.cpu_count())
    ap.add_argument("--out", default="comix-scramble-audit.csv")
    ap.add_argument("--verbose", action="store_true")
    ap.add_argument("--docker-context", default="mediaserver")
    ap.add_argument("--config-dir", default="/opt/mangarr")
    ap.add_argument("--db-name", default="mangarr.db")
    ap.add_argument("--sqlite-image", default="keinos/sqlite3")
    ap.add_argument("--db-local")
    args = ap.parse_args()

    if not args.path_map:
        args.path_map = ["/data/media/=/Users/<user>/Remote/Manga/"]
    maps = []
    for m in args.path_map:
        if "=" not in m:
            sys.exit(f"error: --path-map needs OLD=NEW, got {m!r}")
        old, new = m.split("=", 1)
        maps.append((old, new))
    grids = range(args.grids[0], args.grids[1] + 1)

    # ---- stage 1: download list ----
    if args.from_csv:
        with open(args.from_csv, newline="") as fh:
            rows = list(csv.DictReader(fh))
    else:
        rows = query_downloads(args)
    if not rows:
        sys.exit("error: no downloads found")
    # normalise the FilePath column name and add the local path
    for r in rows:
        fp = r.get("FilePath") or r.get("filepath") or r.get("path") or ""
        r["FilePath"] = fp
        r["LocalPath"] = remap(fp, maps)
    print(f"Stage 1: {len(rows)} '{args.source}' downloads.", file=sys.stderr)

    # ---- stage 2: scan ----
    if not args.no_scan:
        print(f"Stage 2: scanning {len(rows)} CBZ "
              f"(ratio>={args.threshold}, seamabs>={args.seam_floor}, "
              f"fmain>={args.fmain_floor}, workers={args.workers})...", file=sys.stderr)
        scan = {}
        done = 0
        with ProcessPoolExecutor(max_workers=args.workers) as ex:
            futs = {ex.submit(_worker, (r["LocalPath"], args.threshold,
                                        args.seam_floor, args.fmain_floor, grids)): r["LocalPath"]
                    for r in rows}
            for fut in as_completed(futs):
                path, res = fut.result()
                scan[path] = res
                done += 1
                if args.verbose or res["status"] not in ("OK",):
                    extra = f" pages={res['scrambled_pages']}" if res["scrambled_count"] else ""
                    print(f"[{done}/{len(rows)}] {res['status']:9} "
                          f"r={res['max_score']:5.1f} abs={res['max_seamabs']:5.0f}"
                          f"{extra}  {os.path.basename(path)}", file=sys.stderr)
                elif done % 50 == 0:
                    print(f"  ...{done}/{len(rows)}", file=sys.stderr)
        for r in rows:
            res = scan.get(r["LocalPath"], {})
            r["ScrambleStatus"] = res.get("status", "")
            r["ScrambledPages"] = res.get("scrambled_pages", "")
            r["ScrambledCount"] = res.get("scrambled_count", 0)
            r["MaxScore"] = res.get("max_score", "")
            r["MaxSeamAbs"] = res.get("max_seamabs", "")
            r["Detail"] = res.get("detail", "")

    # ---- output ----
    base_cols = ["MangaName", "MangaId", "ChapterNumber", "FilePath", "GrabbedDate"]
    scan_cols = ["ScrambleStatus", "ScrambledPages", "ScrambledCount",
                 "MaxScore", "MaxSeamAbs", "Detail"]
    cols = base_cols + ([] if args.no_scan else scan_cols)
    # scrambled first, then by score
    if not args.no_scan:
        rows.sort(key=lambda r: (r.get("ScrambleStatus") != "SCRAMBLED",
                                 -float(r.get("MaxScore") or 0)))
    with open(args.out, "w", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=cols, extrasaction="ignore")
        w.writeheader()
        w.writerows(rows)

    print("\n===== SUMMARY =====", file=sys.stderr)
    print(f"  source        : {args.source}", file=sys.stderr)
    print(f"  downloads     : {len(rows)}", file=sys.stderr)
    if not args.no_scan:
        n_scr = sum(1 for r in rows if r.get("ScrambleStatus") == "SCRAMBLED")
        n_err = sum(1 for r in rows if r.get("ScrambleStatus") in ("ERROR", "BADZIP", "MISSING"))
        print(f"  scrambled     : {n_scr}", file=sys.stderr)
        print(f"  clean         : {len(rows) - n_scr - n_err}", file=sys.stderr)
        print(f"  errors/missing: {n_err}", file=sys.stderr)
        if n_scr:
            print("\nScrambled CBZs:", file=sys.stderr)
            for r in rows:
                if r.get("ScrambleStatus") == "SCRAMBLED":
                    print(f"  [r={r['MaxScore']:>5} abs={r['MaxSeamAbs']:>4}] "
                          f"pages {r['ScrambledPages']:<12} "
                          f"{r['MangaName']} ch{r['ChapterNumber']}", file=sys.stderr)
    print(f"\n  report        : {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
