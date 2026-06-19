#!/usr/bin/env python3
"""
Full-library stray-chapter audit for Mangarr.

Read-only. For every manga in the library it calls the dry-run prune endpoint
(GET /api/v5/manga/{id}/straychapters) and reports, per manga, what a prune WOULD
remove — without deleting anything. This is the library-wide companion to the
single-manga StrayChapterPruneService cleanup (PR #380).

What a "stray" is (see StrayChapterPruneService): a synthesized Chapter row whose
number is ABOVE the manga's metadata chapter count (Manga.TotalChapterCount). These
are the phantom rows a pre-fix Catalog<->Gateway reconciliation backfilled when a
source mislabeled a few chapters with stray high numbers. Two buckets:

  * file-less strays  -> safe to delete (nothing on disk to lose)
  * with-file strays  -> a mislabeled release may be REAL content; NEVER auto-deleted,
                         only recycle-binned on an explicit deleteFiles=true. These are
                         the rows that need a human look — flagged as REVIEW below.

Manga with no metadata chapter count (TotalChapterCount null/0) can't be audited —
a stray can't be told from a real chapter — and are reported as SKIP.

API facts (Mangarr is a Sonarr fork, REST base /api/v5, X-Api-Key auth):
  - List manga:    GET /api/v5/manga                       -> [{id, title, totalChapterCount, statistics:{chapterCount}}, ...]
  - Dry-run prune: GET /api/v5/manga/{id}/straychapters    -> StrayChapterPruneResource (dryRun=true, never mutates)

Usage:
    export MANGARR_API_KEY=xxxx…                  # or pass --api-key
    python3 scripts/audit-stray-chapters.py       # audit the whole library
    python3 scripts/audit-stray-chapters.py --url http://<redacted-host>:8990 --api-key XXXX
    python3 scripts/audit-stray-chapters.py --all # list every manga, not just the affected ones
    python3 scripts/audit-stray-chapters.py --json report.json   # also write machine-readable output

Nothing here ever POSTs. To actually prune, use the endpoint directly:
    GET  /api/v5/manga/{id}/straychapters                  (manifest)
    POST /api/v5/manga/{id}/straychapters?deleteFiles=true (execute)
"""

import argparse
import os
import sys
import xml.etree.ElementTree as ET
from concurrent.futures import ThreadPoolExecutor, as_completed
from pathlib import Path

try:
    import requests
except ImportError:
    sys.exit("This script needs the 'requests' package:  pip install requests")


def discover_api_key():
    """Best-effort read of the API key from the default Mangarr data dir config.xml."""
    candidates = [
        Path.home() / ".config" / "Mangarr" / "config.xml",
        Path("/config/config.xml"),  # common Docker mount
    ]
    for cfg in candidates:
        if cfg.is_file():
            try:
                key = ET.parse(cfg).getroot().findtext("ApiKey")
                if key:
                    return key.strip()
            except ET.ParseError:
                pass
    return None


class MangarrClient:
    def __init__(self, base_url, api_key, timeout=60):
        self.base = base_url.rstrip("/")
        self.api = f"{self.base}/api/v5"
        self.timeout = timeout
        self.session = requests.Session()
        self.session.headers.update({"X-Api-Key": api_key})

    def list_manga(self):
        r = self.session.get(f"{self.api}/manga", timeout=self.timeout)
        r.raise_for_status()
        return r.json()

    def stray_report(self, manga_id):
        r = self.session.get(f"{self.api}/manga/{manga_id}/straychapters", timeout=self.timeout)
        r.raise_for_status()
        return r.json()


def human_size(num_bytes):
    size = float(num_bytes)
    for unit in ("B", "KB", "MB", "GB", "TB"):
        if size < 1024.0 or unit == "TB":
            return f"{size:,.1f} {unit}"
        size /= 1024.0


# Same density floor the forward fix (ChapterSynthesisService.ResolveDensityCut) uses.
DENSITY_FLOOR = 0.5


def density_cut(baseline, present):
    """Mirror of ChapterSynthesisService.ResolveDensityCut over WITH-FILE evidence.

    `present` is the sorted list of chapter numbers that actually exist on disk above
    `baseline`. Returns the largest number M whose post-baseline density
    |present in (baseline, M]| / (M - baseline) clears the floor — i.e. the top of the
    dense, real cluster. Numbers above the returned cut are sparse far-out outliers
    (the mislabeled-junk shape); numbers at/below it are a legitimate dense extension
    past a stale metadata count. With no dense evidence the cut falls back to baseline.
    """
    above = sorted(n for n in present if n > baseline)
    for top in reversed(above):                      # high -> low
        span = top - baseline
        in_range = sum(1 for n in above if n <= top)
        if span > 0 and in_range / span >= DENSITY_FLOOR:
            return top
    return baseline


def audit_one(client, manga):
    mid = manga["id"]
    title = manga.get("title", f"(id {mid})")
    meta = manga.get("totalChapterCount")
    rows = (manga.get("statistics") or {}).get("chapterCount")
    base = {
        "id": mid, "title": title, "metadataChapterCount": meta, "chapterRows": rows,
        "fileLessStrayCount": 0, "withFileStrayCount": 0, "withFileBytes": 0,
        "withFileNumbers": [], "junkFileLess": 0, "junkWithFile": 0,
        "junkBytes": 0, "junkNumbers": [], "legitExtension": 0,
    }
    try:
        report = client.stray_report(mid)
    except requests.HTTPError as exc:
        return {**base, "status": "ERROR", "error": f"HTTP {exc.response.status_code}"}
    except requests.RequestException as exc:
        return {**base, "status": "ERROR", "error": str(exc)}

    if not report.get("baselineKnown"):
        return {**base, "metadataChapterCount": report.get("metadataChapterCount", meta),
                "status": "SKIP"}

    baseline = report.get("metadataChapterCount") or 0
    file_less = report.get("fileLessStrays", [])
    with_file = report.get("withFileStrays", [])
    fileless_nums = [s.get("chapterNumber") for s in file_less]
    withfile_nums = [s.get("chapterNumber") for s in with_file]
    withfile_bytes_by_num = {
        s.get("chapterNumber"): sum(f.get("size", 0) for f in s.get("files", []))
        for s in with_file
    }

    # The legit cluster top is anchored on WHAT ACTUALLY EXISTS ON DISK (with-file rows),
    # NOT the file-less phantoms — a pre-fix contiguous backfill makes the file-less region
    # look dense even when it's all junk. Numbers above the cut are the true outliers.
    cut = density_cut(baseline, withfile_nums)

    junk_withfile = [n for n in withfile_nums if n > cut]
    junk_fileless = [n for n in fileless_nums if n > cut]
    legit_extension = sum(1 for n in withfile_nums if n <= cut) + \
        sum(1 for n in fileless_nums if n <= cut)
    junk_bytes = sum(withfile_bytes_by_num.get(n, 0) for n in junk_withfile)

    has_disk_evidence_above_baseline = len(withfile_nums) > 0

    if junk_withfile or junk_fileless:
        # Confident junk only when real on-disk evidence anchors the cut. If the cut fell
        # back to baseline because there is NO with-file evidence above it, the file-less
        # rows could be legitimately-wanted chapters from a stale metadata count rather than
        # phantoms — that needs a gateway search to decide, so mark UNCERTAIN, not JUNK.
        status = "JUNK" if has_disk_evidence_above_baseline else "UNCERTAIN"
    elif legit_extension > 0:
        status = "LEGIT-EXT"   # dense real chapters past a stale metadata count — leave alone
    else:
        status = "OK"

    return {
        "id": mid, "title": title,
        "metadataChapterCount": baseline, "chapterRows": rows,
        "baselineKnown": True,
        "fileLessStrayCount": report.get("fileLessStrayCount", 0),
        "withFileStrayCount": report.get("withFileStrayCount", 0),
        "withFileBytes": sum(withfile_bytes_by_num.values()),
        "withFileNumbers": withfile_nums,
        "densityCut": cut,
        "junkFileLess": len(junk_fileless),
        "junkWithFile": len(junk_withfile),
        "junkBytes": junk_bytes,
        "junkNumbers": sorted(junk_withfile),
        "legitExtension": legit_extension,
        "status": status,
    }


def main():
    ap = argparse.ArgumentParser(description="Full-library stray-chapter audit (read-only).")
    ap.add_argument("--url", default=os.environ.get("MANGARR_URL", "http://localhost:8989"),
                    help="Base URL (default: $MANGARR_URL or http://localhost:8989)")
    ap.add_argument("--api-key", default=os.environ.get("MANGARR_API_KEY"),
                    help="API key (default: $MANGARR_API_KEY, else auto-read config.xml)")
    ap.add_argument("--all", action="store_true",
                    help="List every manga, not just those with strays or skipped")
    ap.add_argument("--workers", type=int, default=8,
                    help="Concurrent dry-run requests (default: 8)")
    ap.add_argument("--json", metavar="PATH", default=None,
                    help="Also write the full audit as JSON to PATH")
    args = ap.parse_args()

    api_key = args.api_key or discover_api_key()
    if not api_key:
        sys.exit("No API key. Set MANGARR_API_KEY, pass --api-key, or run where "
                 "config.xml is readable.")

    client = MangarrClient(args.url, api_key)

    try:
        manga = client.list_manga()
    except requests.RequestException as exc:
        sys.exit(f"Failed to list manga from {args.url}: {exc}")

    print(f"Auditing {len(manga)} manga against {args.url} …\n")

    results = []
    with ThreadPoolExecutor(max_workers=max(1, args.workers)) as pool:
        futures = {pool.submit(audit_one, client, m): m for m in manga}
        for fut in as_completed(futures):
            results.append(fut.result())

    # Risk-first sort: confident JUNK on top, then UNCERTAIN, then the rest.
    order = {"JUNK": 0, "UNCERTAIN": 1, "ERROR": 2, "SKIP": 3, "LEGIT-EXT": 4, "OK": 5}
    results.sort(key=lambda r: (
        order.get(r["status"], 9),
        -(r["junkWithFile"] + r["junkFileLess"]),
        -r["fileLessStrayCount"],
        r["id"],
    ))

    shown_statuses = ("JUNK", "UNCERTAIN", "ERROR", "SKIP")
    if args.all:
        shown_statuses = shown_statuses + ("LEGIT-EXT",)
    to_show = [r for r in results if r["status"] in shown_statuses]

    if to_show:
        hdr = (f"{'STATUS':<9} {'ID':>5} {'META':>5} {'ROWS':>5} {'CUT':>5} "
               f"{'JUNK-FL':>7} {'JUNK-WF':>7}  TITLE")
        print(hdr)
        print("-" * len(hdr))
        for r in to_show:
            meta = r["metadataChapterCount"] if r["metadataChapterCount"] is not None else "-"
            rows = r["chapterRows"] if r["chapterRows"] is not None else "-"
            cut = r.get("densityCut", "-")
            title = r["title"]
            if len(title) > 42:
                title = title[:41] + "…"
            extra = ""
            if r["status"] == "JUNK" and r.get("junkNumbers"):
                nums = ",".join(str(n) for n in r["junkNumbers"][:8])
                more = "…" if len(r["junkNumbers"]) > 8 else ""
                extra = f"  [junk files: {nums}{more} | {human_size(r['junkBytes'])}]"
            elif r["status"] == "UNCERTAIN":
                extra = f"  [{r['fileLessStrayCount']} file-less > meta, no on-disk evidence — needs gateway check]"
            elif r["status"] == "ERROR":
                extra = f"  [{r.get('error')}]"
            print(f"{r['status']:<9} {r['id']:>5} {str(meta):>5} {str(rows):>5} {str(cut):>5} "
                  f"{r['junkFileLess']:>7} {r['junkWithFile']:>7}  {title}{extra}")
        print()
    else:
        print("No confident junk found.\n")

    # Library totals.
    n_junk = sum(1 for r in results if r["status"] == "JUNK")
    n_uncertain = sum(1 for r in results if r["status"] == "UNCERTAIN")
    n_legit = sum(1 for r in results if r["status"] == "LEGIT-EXT")
    n_skip = sum(1 for r in results if r["status"] == "SKIP")
    n_error = sum(1 for r in results if r["status"] == "ERROR")
    n_ok = sum(1 for r in results if r["status"] == "OK")

    junk_fileless = sum(r["junkFileLess"] for r in results)
    junk_withfile = sum(r["junkWithFile"] for r in results)
    junk_bytes = sum(r["junkBytes"] for r in results)
    legit_ext_rows = sum(r["legitExtension"] for r in results)
    uncertain_fileless = sum(r["fileLessStrayCount"] for r in results if r["status"] == "UNCERTAIN")
    raw_fileless = sum(r["fileLessStrayCount"] for r in results)
    raw_withfile = sum(r["withFileStrayCount"] for r in results)

    print("=" * 70)
    print("LIBRARY SUMMARY")
    print("=" * 70)
    print(f"  Manga audited                : {len(results)}")
    print(f"  OK (clean)                   : {n_ok}")
    print(f"  LEGIT-EXT (stale metadata)   : {n_legit}  -> {legit_ext_rows} real chapters past a "
          f"stale metadata count — NOT junk, leave alone")
    print(f"  JUNK (confident outliers)    : {n_junk}  -> {junk_fileless} file-less phantom + "
          f"{junk_withfile} with-file ({human_size(junk_bytes)})")
    print(f"  UNCERTAIN (file-less only)   : {n_uncertain}  -> {uncertain_fileless} file-less > metadata, "
          f"no on-disk evidence (could be wanted-legit OR phantom)")
    print(f"  SKIP (no metadata count)     : {n_skip}")
    if n_error:
        print(f"  ERROR (request failed)       : {n_error}")
    print()
    print(f"  NOTE: the prune endpoint's raw signal would touch {raw_fileless} file-less + "
          f"{raw_withfile} with-file rows —")
    print("        but only the JUNK rows above are confident outliers. The LEGIT-EXT rows are")
    print("        real chapters from stale metadata counts; pruning them would delete real content.")
    print()
    if n_junk:
        print("  Confident-junk manga (the Heavenly-Demon shape) are safe to prune per-id:")
        print("    POST /api/v5/manga/{id}/straychapters?deleteFiles=true   (back up first — dev stack hard-deletes)")
    if n_uncertain:
        print("  UNCERTAIN manga: run a MangaSearch first; if the gateway has those chapters they're")
        print("    real-wanted (keep). Only prune if a search confirms they don't exist on any source.")

    if args.json:
        import json as _json
        payload = {
            "url": args.url,
            "densityFloor": DENSITY_FLOOR,
            "totals": {
                "mangaAudited": len(results),
                "ok": n_ok,
                "legitExtension": n_legit,
                "junk": n_junk,
                "uncertain": n_uncertain,
                "skip": n_skip,
                "error": n_error,
                "junkFileLessRows": junk_fileless,
                "junkWithFileRows": junk_withfile,
                "junkBytes": junk_bytes,
                "rawFileLessRows": raw_fileless,
                "rawWithFileRows": raw_withfile,
            },
            "manga": results,
        }
        Path(args.json).write_text(_json.dumps(payload, indent=2))
        print(f"\nWrote JSON report to {args.json}")


if __name__ == "__main__":
    main()
