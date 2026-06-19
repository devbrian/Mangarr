#!/usr/bin/env python3
"""
Full-library cross-title import audit for Mangarr.

Read-only. Length-agnostic. Two complementary detectors over imported ChapterFiles,
both keyed on the TRUE source manga title recovered from `ChapterFiles.OriginalFilePath`
(the gateway's pre-import release path):

  1. CROSS-TITLE   — the file's source title does NOT match the manga it is filed
                     under, nor any of that manga's alternative titles. This is the
                     classic substring / sequel-prefix corruption (e.g. "That Which
                     Flows By" filed under "Flow") that the exact-only resolver guard
                     now blocks going forward.

  2. SHARED-TITLE  — the file's source title DOES match the manga it is under, but that
                     same normalized title is also claimed by one or more OTHER manga.
                     The attribution went through an ambiguous title, so the file could
                     belong to a different manga. This catches the two classes the
                     cross-title pass is blind to:
                       * DUP-TITLE       — >=2 manga share it as a PRIMARY title
                                           (e.g. two distinct "Black Haze" entries).
                       * MISFILED-LIKELY — the source title is ANOTHER single manga's
                                           PRIMARY title; this file probably belongs to
                                           that manga, not the one it is filed under.
                       * ALT-AMBIGUOUS   — no manga owns it as a primary title; >=2 manga
                                           list it only as an ALT — resolved via the
                                           alt-title lookup among co-equal aliases.
                       * OK-PRIMARY      — the manga it is under IS the sole primary owner;
                                           the other claimants merely list it as an alias.
                                           Almost certainly fine — hidden unless --all.

Background (debug session `flow-wrong-manga-not-rejected`, 2026-06-19):
  The query-text-only gateway returns many unrelated titles for a search. Before the
  exact-only resolver guard (MangaParsingService.IsSafeInexactMatch) a short / prefixing
  CleanTitle could substring-match an unrelated release and import it under the wrong
  manga. Title comparison alone can never be airtight (identical / aliased titles are
  ambiguous by definition — the durable cure is stable source-ID targeting, #381), but
  these two passes together surface every EXISTING import whose title evidence is wrong
  or ambiguous. The forward fix prevents NEW occurrences.

How a file is judged:
  source = title from OriginalFilePath (filename head before the " - Chapter" / " - Vol"
           marker; the `manga-0` folder is a gateway placeholder, so the filename — not
           the folder — is authoritative).
  known  = { CleanTitle } U { normalized Title } U { normalized AlternativeTitles }
           U { normalized UserAlternativeTitles }   (per manga)
  - no " - Chapter"/" - Vol" marker            -> UNPARSED (garbled path; not judged)
  - source not in this manga's `known`         -> CROSS-TITLE
  - source in `known` AND claimed by >=2 manga -> SHARED-TITLE (sub-classed above)
  - source in `known` and uniquely this manga  -> genuine (not reported)

Remediation note: wrong files often sit WITHIN [1..TotalChapterCount], so the
stray-chapter prune endpoint (numbers ABOVE TotalChapterCount only) will NOT catch
them. Use --json to drive a backup-first, file-targeted cleanup. SHARED-TITLE hits are
SUSPECTS for human review, not confirmed corruption. This script never mutates anything.

Usage:
    python3 scripts/audit-cross-title-imports.py --db /opt/mangarr/mangarr.db
    python3 scripts/audit-cross-title-imports.py                 # tries default paths
    python3 scripts/audit-cross-title-imports.py --all           # also UNPARSED + OK-PRIMARY
    python3 scripts/audit-cross-title-imports.py --detail        # list each flagged file
    python3 scripts/audit-cross-title-imports.py --json report.json
    # Dev stack (DB on the mediaserver): run it there over SSH —
    #   ssh <user>@<mediaserver> 'python3 -' < scripts/audit-cross-title-imports.py
    # or copy the mangarr.db locally and pass --db.
"""

import argparse
import json
import os
import re
import sqlite3
import sys
import unicodedata
from collections import defaultdict
from pathlib import Path

# ---------------------------------------------------------------------------
# Title normalization — faithful port of
# NzbDrone.Core.Parser.Manga.MangaTitleNormalizer.Normalize (the comparison form):
#   1. drop a trailing parenthetical / bracketed alt-title suffix
#   2. NFKD, strip combining diacritic marks
#   3. lowercase, keep letters/digits/whitespace (punctuation dropped, NO
#      replacement char — so "Flat & Flow" -> "flat flow", "My/Hero" -> "myhero")
#   4. collapse whitespace runs
# CleanTitle / AlternativeTitles are stored already-normalized by the app; we run the
# same algorithm on the recovered source title so the comparison is symmetric.
# ---------------------------------------------------------------------------
_ALT_SUFFIX = re.compile(r"\s*[\(\[][^\)\]]*[\)\]]\s*$")


def normalize(title):
    if not title:
        return ""
    title = _ALT_SUFFIX.sub("", title)
    nfkd = unicodedata.normalize("NFKD", title)
    stripped = "".join(c for c in nfkd if unicodedata.category(c) != "Mn")
    lowered = stripped.lower()
    kept = "".join(c if (c.isalnum() or c.isspace()) else "" for c in lowered)
    return " ".join(kept.split())


# ---------------------------------------------------------------------------
# Source-title recovery from OriginalFilePath.
#
# Gateway shapes seen in the wild:
#   /data/manga/manga-That Which Flows By/That Which Flows By - Chapter 80 (en)-<hash>.cbz
#   /data/manga/manga-0/Versatile Mage - Chapter 1126 (en).cbz            (manga-0 placeholder)
#   /data/manga/manga-0/Hanzou ... - Vol. 1 Chapter 4 (en) [Group].cbz
#   /data/manga/manga-0/nonymous].cbz                                     (truncated/garbled)
#
# The filename is authoritative (the folder is often the `manga-0` placeholder). The
# title is everything before the " - Chapter" / " - Vol" / " - Ch" marker. A file with
# no such marker has an unrecoverable source and is classed UNPARSED, never flagged.
# ---------------------------------------------------------------------------
_MARKER = re.compile(r"\s-\s(?:chapter\b|vol\b|vol\.|ch\b|ch\.)", re.IGNORECASE)
_HASH_EXT = re.compile(r"-[0-9a-f]{6,}\.(?:cbz|cbr|cb7|cbt|zip|rar|pdf|epub)$", re.IGNORECASE)
_EXT = re.compile(r"\.(?:cbz|cbr|cb7|cbt|zip|rar|pdf|epub)$", re.IGNORECASE)


def recover_source_title(original_file_path):
    """Return (source_title, marker_found). marker_found=False => UNPARSED."""
    base = (original_file_path or "").replace("\\", "/").split("/")[-1]
    base = _HASH_EXT.sub("", base)
    base = _EXT.sub("", base)
    parts = _MARKER.split(base, 1)
    if len(parts) == 1:
        # No chapter/volume marker -> the source title can't be trusted.
        return "", False
    head = _ALT_SUFFIX.sub("", parts[0])
    return head, True


DEFAULT_DB_PATHS = [
    os.environ.get("MANGARR_DB"),
    str(Path.home() / ".config" / "Mangarr" / "mangarr.db"),
    "/config/mangarr.db",
    "/opt/mangarr/mangarr.db",
    "mangarr.db",
]


def find_db(explicit):
    for p in ([explicit] if explicit else DEFAULT_DB_PATHS):
        if p and Path(p).is_file():
            return p
    return None


def load_titles(cur):
    """Return (manga, claim_all, claim_clean).

    manga       : mid -> {clean, title, names(set of all normalized titles)}
    claim_all   : normalized title -> set(mid) that hold it as ANY title (clean/alt)
    claim_clean : normalized title -> set(mid) that hold it as a PRIMARY title
                  (CleanTitle or the normalized display Title)
    """
    manga = {}
    claim_all = defaultdict(set)
    claim_clean = defaultdict(set)
    for mid, title, clean, alt, ualt in cur.execute(
        "SELECT Id, Title, CleanTitle, AlternativeTitles, UserAlternativeTitles FROM Manga"
    ):
        clean_norm = (clean or "").strip()
        ntitle = normalize(title)
        primaries = {t for t in (clean_norm, ntitle) if t}
        names = set(primaries)
        for col in (alt, ualt):
            if not col:
                continue
            try:
                for x in json.loads(col):
                    n = normalize(x)
                    if n:
                        names.add(n)
            except (ValueError, TypeError):
                pass
        manga[mid] = {"clean": clean_norm or ntitle, "title": title or f"(id {mid})", "names": names}
        for t in names:
            claim_all[t].add(mid)
        for t in primaries:
            claim_clean[t].add(mid)
    return manga, claim_all, claim_clean


def classify_shared(mid, nsrc, claim_all, claim_clean):
    """Sub-class a file whose source title is claimed by >=2 distinct manga."""
    clean_owners = claim_clean.get(nsrc, set())
    rivals = sorted(claim_all.get(nsrc, set()) - {mid})
    if len(clean_owners) >= 2:
        return "DUP-TITLE", sorted(clean_owners - {mid}) or sorted(clean_owners), rivals
    if clean_owners and mid not in clean_owners:
        return "MISFILED-LIKELY", sorted(clean_owners), rivals
    if mid in clean_owners:
        return "OK-PRIMARY", [mid], rivals
    return "ALT-AMBIGUOUS", rivals, rivals


def audit(db_path):
    con = sqlite3.connect(f"file:{db_path}?mode=ro&immutable=1", uri=True)
    cur = con.cursor()
    manga, claim_all, claim_clean = load_titles(cur)

    per = defaultdict(lambda: {"total": 0, "unparsed": 0,
                               "wrong_sources": defaultdict(int), "files": []})
    shared = defaultdict(lambda: {"subclass": None, "rivals": set(),
                                  "likely": [], "bytes": 0, "files": []})

    for cf_id, mid, ch_id, opath, relpath, size in cur.execute(
        "SELECT Id, MangaId, ChapterId, OriginalFilePath, RelativePath, Size FROM ChapterFiles"
    ):
        rec = per[mid]
        rec["total"] += 1
        src, marker = recover_source_title(opath)
        nsrc = normalize(src) if marker else ""
        if not nsrc:
            rec["unparsed"] += 1
            continue
        names = manga.get(mid, {}).get("names", set())
        if nsrc not in names:
            # Detector 1: cross-title — source is a different title entirely.
            rec["wrong_sources"][nsrc] += 1
            rec["files"].append({
                "chapterFileId": cf_id, "chapterId": ch_id,
                "relativePath": relpath, "sourceTitle": src.strip(),
                "normalizedSource": nsrc, "size": size or 0,
            })
            continue
        # Detector 2: shared-title — source matches THIS manga but is also claimed elsewhere.
        if len(claim_all.get(nsrc, ())) >= 2:
            subclass, likely, rivals = classify_shared(mid, nsrc, claim_all, claim_clean)
            s = shared[(mid, nsrc)]
            s["subclass"] = subclass
            s["rivals"] = set(rivals)
            s["likely"] = likely
            s["bytes"] += size or 0
            s["files"].append({
                "chapterFileId": cf_id, "chapterId": ch_id,
                "relativePath": relpath, "sourceTitle": src.strip(), "size": size or 0,
            })
    con.close()

    # --- assemble cross-title results -------------------------------------
    cross = []
    for mid, rec in per.items():
        if not rec["files"]:
            continue
        info = manga.get(mid, {"clean": "", "title": f"(id {mid})"})
        c = info["clean"].replace(" ", "")
        srcs = rec["wrong_sources"].keys()
        all_contain = bool(c) and all(c in s.replace(" ", "") for s in srcs)
        cross.append({
            "id": mid, "title": info["title"], "cleanTitle": info["clean"],
            "totalFiles": rec["total"], "mismatchCount": len(rec["files"]),
            "classification": "SUBSTR" if all_contain else "MIXED",
            "wrongSources": dict(sorted(rec["wrong_sources"].items(), key=lambda kv: -kv[1])),
            "wrongBytes": sum(f["size"] for f in rec["files"]),
            "files": rec["files"],
        })
    cross.sort(key=lambda r: -r["mismatchCount"])

    # --- assemble shared-title results ------------------------------------
    def name_of(mid):
        return manga.get(mid, {}).get("title", f"(id {mid})")

    shared_list = []
    for (mid, nsrc), s in shared.items():
        shared_list.append({
            "id": mid, "title": name_of(mid), "sourceTitle": nsrc,
            "subclass": s["subclass"], "count": len(s["files"]),
            "bytes": s["bytes"],
            "rivals": [{"id": r, "title": name_of(r)} for r in sorted(s["rivals"])],
            "likelyOwner": [{"id": r, "title": name_of(r)} for r in s["likely"]],
            "files": s["files"],
        })
    rank = {"DUP-TITLE": 0, "MISFILED-LIKELY": 1, "ALT-AMBIGUOUS": 2, "OK-PRIMARY": 3}
    shared_list.sort(key=lambda r: (rank.get(r["subclass"], 9), -r["count"]))

    unparsed = {mid: rec["unparsed"] for mid, rec in per.items() if rec["unparsed"]}
    total_files = sum(rec["total"] for rec in per.values())
    return cross, shared_list, unparsed, total_files, manga


def main():
    ap = argparse.ArgumentParser(
        description="Full-library cross-title + shared-title import audit (read-only).")
    ap.add_argument("--db", default=None,
                    help="Path to mangarr.db (default: $MANGARR_DB or common locations)")
    ap.add_argument("--min", type=int, default=1, metavar="N",
                    help="Only report cross-title manga with >= N mismatched files (default: 1)")
    ap.add_argument("--all", action="store_true",
                    help="Also list UNPARSED (garbled-path) manga and OK-PRIMARY shared hits")
    ap.add_argument("--detail", action="store_true",
                    help="List each flagged file (chapterFileId + relative path + source)")
    ap.add_argument("--json", metavar="PATH", default=None,
                    help="Also write the full audit (incl. per-file lists) as JSON")
    args = ap.parse_args()

    db_path = find_db(args.db)
    if not db_path:
        sys.exit("No mangarr.db found. Pass --db PATH or set $MANGARR_DB. "
                 "On the dev stack the DB is /opt/mangarr/mangarr.db on the mediaserver "
                 "(run this script there over SSH, or copy the DB locally).")

    cross, shared_list, unparsed, total_files, manga = audit(db_path)
    cross = [r for r in cross if r["mismatchCount"] >= args.min]

    print(f"Cross-title + shared-title import audit  (DB: {db_path})")
    print(f"Scanned {total_files} ChapterFiles across {len(manga)} manga.\n")

    # -------- Detector 1: cross-title -------------------------------------
    print("#" * 70)
    print("# 1. CROSS-TITLE IMPORTS  (source title != the manga it is filed under)")
    print("#" * 70)
    if cross:
        hdr = f"{'CLASS':<7} {'ID':>5} {'WRONG':>6}/{'TOT':<6} {'TITLE':<32} WRONG SOURCES"
        print(hdr)
        print("-" * len(hdr))
        for r in cross:
            title = r["title"] if len(r["title"]) <= 31 else r["title"][:30] + "…"
            ws = list(r["wrongSources"].items())
            shown = "; ".join(f"{k}({v})" for k, v in ws[:4])
            more = f" +{len(ws) - 4}" if len(ws) > 4 else ""
            print(f"{r['classification']:<7} {r['id']:>5} {r['mismatchCount']:>6}/"
                  f"{r['totalFiles']:<6} {title:<32} {shown}{more}")
            if args.detail:
                for f in r["files"][:30]:
                    print(f"          - cf#{f['chapterFileId']} ch#{f['chapterId']} "
                          f"src={f['sourceTitle']!r} -> {f['relativePath']}")
                if len(r["files"]) > 30:
                    print(f"          … +{len(r['files']) - 30} more files")
    else:
        print("None. ✅")
    print()

    # -------- Detector 2: shared-title ------------------------------------
    show_subclasses = {"DUP-TITLE", "MISFILED-LIKELY", "ALT-AMBIGUOUS"}
    if args.all:
        show_subclasses = show_subclasses | {"OK-PRIMARY"}
    shared_shown = [r for r in shared_list if r["subclass"] in show_subclasses]

    print("#" * 70)
    print("# 2. SHARED-TITLE IMPORTS  (source matches this manga but is claimed by others)")
    print("#" * 70)
    if shared_shown:
        hdr = f"{'SUBCLASS':<16} {'ID':>5} {'FILES':>6} {'SOURCE TITLE':<28} RIVAL / LIKELY-TRUE-OWNER"
        print(hdr)
        print("-" * len(hdr))
        for r in shared_shown:
            src = r["sourceTitle"] if len(r["sourceTitle"]) <= 27 else r["sourceTitle"][:26] + "…"
            owners = r["likelyOwner"] if r["subclass"] in ("MISFILED-LIKELY", "DUP-TITLE") else r["rivals"]
            who = "; ".join(f"id{o['id']}({o['title'][:22]})" for o in owners[:3])
            print(f"{r['subclass']:<16} {r['id']:>5} {r['count']:>6} {src:<28} {who}")
            if args.detail:
                for f in r["files"][:20]:
                    print(f"          - cf#{f['chapterFileId']} ch#{f['chapterId']} -> {f['relativePath']}")
                if len(r["files"]) > 20:
                    print(f"          … +{len(r['files']) - 20} more files")
        print()
        print("  Legend: DUP-TITLE = >=2 manga share this PRIMARY title (e.g. two 'Black Haze');")
        print("          MISFILED-LIKELY = source is ANOTHER manga's primary title (probably belongs there);")
        print("          ALT-AMBIGUOUS = resolved among >=2 co-equal alt-title aliases;")
        print("          OK-PRIMARY (hidden unless --all) = this manga is the sole primary owner.")
        print("  These are SUSPECTS for human review — NOT confirmed corruption.")
    else:
        print("None. ✅")
    print()

    if args.all and unparsed:
        print("UNPARSED-only files (garbled/truncated OriginalFilePath — source unrecoverable):")
        for mid in sorted(unparsed, key=lambda m: -unparsed[m]):
            print(f"  id {mid:>5}  {unparsed[mid]:>4} garbled-path files  {manga.get(mid, {}).get('title', '')}")
        print()

    # -------- summary -----------------------------------------------------
    cross_files = sum(r["mismatchCount"] for r in cross)
    cross_bytes = sum(r["wrongBytes"] for r in cross)
    by_sub = defaultdict(lambda: [0, 0])  # subclass -> [manga, files]
    for r in shared_list:
        by_sub[r["subclass"]][0] += 1
        by_sub[r["subclass"]][1] += r["count"]
    total_unparsed = sum(unparsed.values())

    print("=" * 70)
    print("SUMMARY")
    print("=" * 70)
    print(f"  CROSS-TITLE      : {len(cross)} manga / {cross_files} files "
          f"({cross_bytes / 1e9:.2f} GB)  — wrong title, high confidence")
    for sub in ("DUP-TITLE", "MISFILED-LIKELY", "ALT-AMBIGUOUS", "OK-PRIMARY"):
        m, f = by_sub.get(sub, [0, 0])
        if m or sub != "OK-PRIMARY":
            note = " (review)" if sub in ("DUP-TITLE", "MISFILED-LIKELY", "ALT-AMBIGUOUS") else " (likely fine)"
            print(f"  {sub:<16} : {m} manga / {f} files{note}")
    print(f"  UNPARSED         : {total_unparsed} files (garbled paths — source unrecoverable)")
    print()
    print("  Title-based detection cannot be airtight (identical/aliased titles are ambiguous")
    print("  by definition); the durable cure is stable source-ID targeting (#381). Wrong files")
    print("  often sit WITHIN [1..TotalChapterCount], so the stray-prune endpoint won't catch")
    print("  them — use --json for a file-targeted, backup-first cleanup. This script never mutates.")

    if args.json:
        payload = {
            "db": db_path,
            "totals": {
                "filesScanned": total_files, "mangaScanned": len(manga),
                "crossTitleManga": len(cross), "crossTitleFiles": cross_files,
                "crossTitleBytes": cross_bytes,
                "sharedTitle": {k: {"manga": v[0], "files": v[1]} for k, v in by_sub.items()},
                "unparsedFiles": total_unparsed,
            },
            "crossTitle": cross,
            "sharedTitle": shared_list,
        }
        Path(args.json).write_text(json.dumps(payload, indent=2, ensure_ascii=False), encoding="utf-8")
        print(f"\nWrote JSON report to {args.json}")


if __name__ == "__main__":
    main()
