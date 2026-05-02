#requires -Version 7.0
<#
.SYNOPSIS
  Regenerate the 500-title manga parser test corpus.

.DESCRIPTION
  Pulls real MangaDex /chapter feed entries and writes a 500-entry JSON fixture to
  src/NzbDrone.Core.Test/Parser/Manga/test_corpus_v1.json.

  This script is run MANUALLY when the corpus needs to be refreshed (e.g., after
  fixing a parser bug that surfaces a missing edge case). It is NOT run at test
  time — see RESEARCH.md Pitfall 8 ("corpus committed in-tree, not regenerated
  at test time").

  The composition target is:
    >=350 en, >=50 es, >=30 ja, >=30 raw, >=40 decimals, >=20 Extras, >=10 oneshots

.NOTES
  Phase 2 ships a deterministic, in-repo generator (committed corpus). When this
  script is later wired up to call the real MangaDex API, replace the body below.
#>

param(
    [string]$Output = (Join-Path $PSScriptRoot "../src/NzbDrone.Core.Test/Parser/Manga/test_corpus_v1.json")
)

Write-Host "Manual corpus refresh: not yet wired up to live MangaDex API."
Write-Host "Phase 2 ships the deterministic in-repo corpus at:"
Write-Host "  $Output"
Write-Host ""
Write-Host "To extend the corpus, edit the generator under .planning/phases/02-parser-metadata-sources/"
Write-Host "or add new entries directly to test_corpus_v1.json. Then re-run dotnet test to verify the"
Write-Host ">=95%% parse rate gate (D-08) still holds."
