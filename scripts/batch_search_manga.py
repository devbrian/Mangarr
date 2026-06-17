#!/usr/bin/env python3
"""
Batch manga search driver for Mangarr.

Searches all manga in batches of N (default 5), ordered so the "smallest" titles
go first. The sort key is (downloaded chapters, total chapters) ascending, i.e.:

    0/1, 0/2, ... 0/10, ... 0/100, ... then 1/2, 1/5, 1/10, 1/10000, ...

After dispatching each batch of MangaSearch commands, the script blocks only until
the number of in-flight (Queued/Started) commands drops to --max-in-flight (default
2) or fewer, then dispatches the next batch. This keeps the pipeline topped up
instead of waiting for a full drain between batches.

API facts (Mangarr is a Sonarr fork, REST base /api/v5, X-Api-Key auth):
  - Trigger search:   POST /api/v5/command  {"name": "MangaSearch", "mangaIds": [..]}
                      (per-manga bulk; backend searches all missing monitored chapters)
  - Command status:   GET  /api/v5/command/{id}  -> {"status": "queued"|"started"|
                      "completed"|"failed"|"aborted"|"cancelled"|"orphaned", ...}
  - All commands:     GET  /api/v5/command       -> [CommandResource, ...]
  - List manga:       GET  /api/v5/manga         -> [{id, title, statistics:{...}}, ...]
      statistics.chapterFileCount   = downloaded chapter count
      statistics.totalChapterCount  = total chapters (metadata source)

Usage:
    export MANGARR_API_KEY=xxxx…              # or pass --api-key
    python3 batch_search_manga.py             # search everything, 5 at a time
    python3 batch_search_manga.py --batch-size 5 --url http://localhost:8989
    python3 batch_search_manga.py --dry-run   # print the plan, dispatch nothing
"""

import argparse
import os
import sys
import time
import xml.etree.ElementTree as ET
from pathlib import Path

try:
    import requests
except ImportError:
    sys.exit("This script needs the 'requests' package:  pip install requests")


# Command statuses that mean a command is still in flight.
ACTIVE_STATUSES = {"queued", "started"}
# Integer enum fallback (CommandStatus.cs): Queued=0, Started=1.
ACTIVE_STATUS_INTS = {0, 1}


def normalize_status(status):
    """Return a lowercase status string whether the API gave us a string or an int."""
    if isinstance(status, int):
        return {0: "queued", 1: "started", 2: "completed", 3: "failed",
                4: "aborted", 5: "cancelled", 6: "orphaned"}.get(status, str(status))
    return str(status).strip().lower()


def is_active(status):
    if isinstance(status, int):
        return status in ACTIVE_STATUS_INTS
    return normalize_status(status) in ACTIVE_STATUSES


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
    def __init__(self, base_url, api_key, timeout=30):
        self.base = base_url.rstrip("/")
        self.api = f"{self.base}/api/v5"
        self.timeout = timeout
        self.session = requests.Session()
        self.session.headers.update({"X-Api-Key": api_key,
                                     "Content-Type": "application/json"})

    def list_manga(self):
        r = self.session.get(f"{self.api}/manga", timeout=self.timeout)
        r.raise_for_status()
        return r.json()

    def search_manga(self, manga_ids):
        """POST a single MangaSearch command for the given manga ids. Returns command id."""
        payload = {"name": "MangaSearch", "mangaIds": list(manga_ids)}
        r = self.session.post(f"{self.api}/command", json=payload, timeout=self.timeout)
        r.raise_for_status()
        return r.json().get("id")

    def active_commands(self):
        """All commands currently Queued or Started."""
        r = self.session.get(f"{self.api}/command", timeout=self.timeout)
        r.raise_for_status()
        return [c for c in r.json() if is_active(c.get("status"))]


def sort_key(manga):
    """(downloaded, total) ascending. Missing stats sort as 0."""
    stats = manga.get("statistics") or {}
    downloaded = stats.get("chapterFileCount", 0) or 0
    total = stats.get("totalChapterCount", 0) or 0
    return (downloaded, total)


def chunked(seq, size):
    for i in range(0, len(seq), size):
        yield seq[i:i + size]


def wait_for_capacity(client, threshold, poll_interval, quiet):
    """Block until the number of in-flight (Queued/Started) commands is <= threshold."""
    while True:
        active = client.active_commands()
        if len(active) <= threshold:
            return
        if not quiet:
            names = ", ".join(sorted({c.get("name", "?") for c in active}))
            print(f"    … {len(active)} command(s) in flight ({names}); "
                  f"waiting for <= {threshold} (poll {poll_interval}s)", flush=True)
        time.sleep(poll_interval)


def main():
    ap = argparse.ArgumentParser(description="Batch-search all Mangarr manga, smallest first.")
    ap.add_argument("--url", default=os.environ.get("MANGARR_URL", "http://localhost:8989"),
                    help="Mangarr base URL (default: %(default)s)")
    ap.add_argument("--api-key", default=os.environ.get("MANGARR_API_KEY"),
                    help="API key (default: $MANGARR_API_KEY, else auto-read config.xml)")
    ap.add_argument("--batch-size", type=int, default=5,
                    help="Manga per batch (default: %(default)s)")
    ap.add_argument("--poll-interval", type=float, default=5.0,
                    help="Seconds between queue polls (default: %(default)s)")
    ap.add_argument("--max-in-flight", type=int, default=2,
                    help="Fire the next batch once this many or fewer commands are "
                         "still running (default: %(default)s)")
    ap.add_argument("--skip", type=int, default=0,
                    help="Skip the first N manga after sorting (resume / offset)")
    ap.add_argument("--limit", type=int, default=None,
                    help="Only process the first N manga after sorting+skipping (for testing)")
    ap.add_argument("--dry-run", action="store_true",
                    help="Print the ordered plan and exit without dispatching anything")
    ap.add_argument("--quiet", action="store_true", help="Less chatter while waiting")
    args = ap.parse_args()

    api_key = args.api_key or discover_api_key()
    if not api_key:
        sys.exit("No API key. Set MANGARR_API_KEY, pass --api-key, or run where "
                 "config.xml is readable.")

    client = MangarrClient(args.url, api_key)

    print(f"Fetching manga from {client.api} …", flush=True)
    manga = client.list_manga()
    manga.sort(key=sort_key)
    if args.skip:
        if args.skip < 0:
            sys.exit("--skip must be >= 0")
        print(f"Skipping first {args.skip} manga.")
        manga = manga[args.skip:]
    if args.limit is not None:
        manga = manga[:args.limit]

    if not manga:
        print("No manga found. Nothing to do.")
        return

    print(f"{len(manga)} manga, ordered (downloaded/total) ascending:")
    for m in manga:
        s = m.get("statistics") or {}
        print(f"  [{m['id']:>5}] {s.get('chapterFileCount', 0)}/"
              f"{s.get('totalChapterCount', 0)}  {m.get('title', '?')}")

    batches = list(chunked(manga, args.batch_size))
    print(f"\n{len(batches)} batch(es) of up to {args.batch_size}.")

    if args.dry_run:
        print("\n--dry-run: no commands dispatched.")
        return

    for n, batch in enumerate(batches, start=1):
        ids = [m["id"] for m in batch]
        titles = ", ".join(m.get("title", str(m["id"])) for m in batch)
        print(f"\n=== Batch {n}/{len(batches)} — searching {len(ids)} manga: {titles}",
              flush=True)
        cmd_id = client.search_manga(ids)
        print(f"    dispatched MangaSearch command id={cmd_id} for mangaIds={ids}",
              flush=True)

        # Don't wait for a full drain — fire the next batch once the in-flight
        # command count drops to the threshold (default 2 or fewer).
        if n < len(batches):
            wait_for_capacity(client, args.max_in_flight, args.poll_interval, args.quiet)
            print(f"    batch {n} dispatched; queue at/under "
                  f"{args.max_in_flight} in flight.", flush=True)

    print("\nAll batches complete.")


if __name__ == "__main__":
    main()
