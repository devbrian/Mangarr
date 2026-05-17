#!/usr/bin/env python3
"""scripts/trim-locale-keys-phase-23.py — Phase 23 locale sweep.

Removes the 6 user-locked dead key families from every locale JSON file under
``src/NzbDrone.Core/Localization/Core/``.

Strict scope per D-11 — exactly these 6 strings (no broader audit):
  UsenetDelay
  UsenetDelayHelpText
  UsenetDelayTime
  TorrentDelay
  TorrentDelayHelpText
  TorrentDelayTime

Style discipline (Pitfall 4 — CodeRabbit JSON-formatting gate):
  • 2-space indent (matches the 35 of 45 locale files that use 2-space; the
    other 10 files use 4-space but at HEAD 42adc910a NONE of those files
    contain any of the 6 dead keys, so they are untouched by this sweep —
    verified at script-author time).
  • ``sort_keys=True`` — every affected locale file is already alphabetically
    sorted, so the sort is a NO-OP on order (verified at script-author time
    via per-file ``sorted(keys) == keys`` check).
  • Trailing newline preserved.
  • A locale file is REWRITTEN only when at least one dead key was actually
    removed — files with zero matches are left bit-identical on disk to
    avoid spurious style normalization on the 10 four-space-indent files
    that do not carry any dead key.

Idempotency: re-running the script after a successful first run results in
zero file changes (per-file ``removed=0`` for every file).

D-11 + Pattern 4 invariants:
  • Script committed alongside the diff for review-ability.
  • Idempotent on re-run.
  • Strict scope — no other keys touched.

Usage:
  python scripts/trim-locale-keys-phase-23.py
"""

import json
import sys
from pathlib import Path

DEAD_KEYS = {
    "UsenetDelay",
    "UsenetDelayHelpText",
    "UsenetDelayTime",
    "TorrentDelay",
    "TorrentDelayHelpText",
    "TorrentDelayTime",
}

LOCALE_DIR = Path("src/NzbDrone.Core/Localization/Core")


def main() -> int:
    if not LOCALE_DIR.is_dir():
        print(f"FATAL: locale dir not found: {LOCALE_DIR}", file=sys.stderr)
        return 1

    total_removed = 0
    files_modified = 0
    files_scanned = 0

    for f in sorted(LOCALE_DIR.glob("*.json")):
        files_scanned += 1
        original_text = f.read_text(encoding="utf-8")
        data = json.loads(original_text)
        before = len(data)
        for k in list(data.keys()):
            if k in DEAD_KEYS:
                del data[k]
        removed = before - len(data)
        total_removed += removed

        if removed == 0:
            # Bit-identical write-skip — protects 4-space-indent files that
            # carry no dead keys from getting style-normalized to 2-space.
            print(f"{f.name}: removed 0 (skipped — no dead keys present)")
            continue

        # Preserve sort_keys=True + 2-space indent + trailing newline per
        # Pitfall 4. Verified at script-author time: every file that
        # actually contains a dead key uses 2-space indent and is already
        # alphabetically sorted.
        new_text = json.dumps(data, ensure_ascii=False, indent=2, sort_keys=True) + "\n"
        if new_text != original_text:
            f.write_text(new_text, encoding="utf-8")
            files_modified += 1
        print(f"{f.name}: removed {removed}")

    print(
        f"Total: scanned={files_scanned} modified={files_modified} "
        f"removed={total_removed} key entries"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
