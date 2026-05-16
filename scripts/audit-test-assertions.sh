#!/usr/bin/env bash
# Phase 18 TEST-UI-02 anti-pattern gate + GH #180 selector-strategy gate.
#
# Gate 1 (Phase 18 TEST-UI-02): Greps every [Test] in
# src/NzbDrone.Automation.Test/Tests/**/*.cs. A test is the anti-pattern
# if its body contains ONLY `ToBeVisibleAsync()` / `ToBeAttachedAsync()`
# assertions with NO state assertion (none of: ToHaveTextAsync,
# ToHaveValueAsync, ToEqualAsync, .Should(), WaitForAsync,
# TextContentAsync, GetByTestId chained, etc.).
#
# Per .planning/phases/18-.../18-VALIDATION.md §"Meta-Validation" point 3 +
# memory feedback_verify_ui_state_not_just_rendering.md (visible rows can
# hide silent rejection icons — every action-asserting test MUST verify
# state, not just rendering).
#
# Allowlist: methods named `loads_*` (page-load route fixtures by convention)
# and fixtures with `PageLoadFixture` in path are exempt — these are
# intentionally rendering-only smokes for the route axis.
# `[Explicit]`-attributed tests are also exempt (cassette-deferred or
# dependency-deferred per Plan-04/05/08 deferred-items.md).
#
# Gate 2 (GH #180 selector-strategy): bans the two wrapper-bypass
# patterns under src/NzbDrone.Automation.Test/Tests/Settings/**:
#   - Locator("input[name=...")           — bypasses the
#     `settings-{provider}-field-{name}` testid contract emitted by
#     FormInputGroup / ProviderFieldFormGroup (issue #180 scopes C+D).
#   - Locator("label:has(input...")       — bypasses the CheckInput
#     wrapping-<label> testid emitted on the per-row checkboxes
#     (issue #180 scopes A+B).
# Both patterns produced fragile selectors that broke whenever the
# wrapper-layer DOM shifted (Phase 18 Plan-04 wrapper sweep precedent).
# The ban is scoped to Tests/Settings/ because (a) Settings is the
# cluster the testid-sweep targets and (b) Manga / Activity / Components
# fixtures use these patterns for orthogonal reasons (the AddManga modal
# uses `input[name='showMonitored']` to anchor a modal-internal input
# without a discrete testid; out of scope for #180).

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
TESTS_DIR="$REPO_ROOT/src/NzbDrone.Automation.Test/Tests"

[[ -d "$TESTS_DIR" ]] || { echo "FAIL: Tests dir missing: $TESTS_DIR" >&2; exit 1; }

VIOLATING_FILES=()

# ----------------------------------------------------------------------
# Gate 1 — visibility-only assertion anti-pattern
# ----------------------------------------------------------------------

# Iterate every .cs file under Tests/ and call the python audit per-file.
# A non-zero exit from python indicates the file violates the anti-pattern gate.
while IFS= read -r -d '' file; do
  set +e
  python3 - "$file" <<'PYEOF'
import re, sys
path = sys.argv[1]
with open(path, encoding='utf-8') as f:
    src = f.read()

# BL-04 fix (Plan 18-13): pre-strip line comments AND block comments BEFORE
# the class_explicit_pat check, so an `// [Explicit] ...` mention in a
# comment block above the class declaration cannot accidentally exempt the
# fixture from the gate. (Previously this strip happened later, AFTER the
# class_explicit_pat search, which made the bypass trivial.)
src_no_comments = re.sub(r'^\s*//.*$', '', src, flags=re.MULTILINE)
src_no_comments = re.sub(r'/\*[\s\S]*?\*/', '', src_no_comments)

# Class-level [Explicit] attribute exempts every method in the fixture.
# These are documented deferrals (Plan-04 cassette, Plan-08 dependencies).
# The attribute must be on its own line directly above the `public class`
# declaration, with only other attribute lines (e.g. [TestFixture], [Category])
# allowed between. MULTILINE-anchored; no DOTALL so [\s\S] won't span the body.
class_explicit_pat = re.compile(
    r'^\s*\[Explicit[^\]]*\]\s*\n(?:\s*\[[^\]]+\][^\n]*\n)*\s*public\s+class\s+\w+',
    re.MULTILINE
)
if class_explicit_pat.search(src_no_comments):
    sys.exit(0)

# Fixtures whose path ends in PageLoadFixture are intentional rendering-only
# route-axis smokes — exempt from state-assertion requirement.
if 'PageLoadFixture' in path:
    sys.exit(0)

# Find [Test] attributed methods. The method body terminates at the first `}`
# at 4-space indent (NUnit fixture convention; verified across the Phase 18
# Tests/ tree). Includes async Task and void variants.
method_pat = re.compile(
    r'\[Test\][^\n]*\n'
    r'(?:\s*\[[^\]]+\][^\n]*\n)*'                            # optional sibling attrs (e.g. [Explicit])
    r'\s*public\s+(?:async\s+)?(?:Task|void)\s+(\w+)\s*\([^)]*\)\s*\{'
    r'(.*?)\n    \}',
    re.DOTALL
)

state_assertion_tokens = [
    # Playwright state assertions
    'ToHaveTextAsync', 'ToHaveValueAsync', 'ToHaveAttributeAsync',
    'ToHaveCountAsync', 'ToHaveURLAsync', 'ToHaveTitleAsync',
    'ToEqualAsync',
    'TextContentAsync', 'InputValueAsync', 'GetAttributeAsync',
    'CountAsync', 'WaitForAsync',
    # FluentAssertions state predicates
    '.Should().Be', '.Should().Contain', '.Should().HaveCount',
    '.Should().EndWith', '.Should().StartWith', '.Should().Match',
    '.Should().NotBeNull', '.Should().NotBeEmpty',
    '.Should().BeGreater', '.Should().BeLess',
    # NUnit deferral idioms (Plan-09 + Plan-08 use Inconclusive/Ignore as
    # explicit-defer markers; not the anti-pattern this gate targets)
    'Assert.Inconclusive', 'Assert.Ignore', 'Assert.Pass',
    # Backend round-trip state inspection (Plan-04+ flows)
    'await Api', 'await _api', 'TestKit.', 'TestKit(',
    # State-discriminating selectors (per memory feedback_verify_ui_state)
    '.Decision', '.Rejected', '.Status',
]
visibility_only_tokens = ['ToBeVisibleAsync', 'ToBeAttachedAsync']

# Note: `src_no_comments` is defined above (before the class_explicit_pat
# check) as part of the BL-04 fix from Plan 18-13. The downstream method_pat
# scan uses the same comment-stripped source.

anti_methods = []
for m in method_pat.finditer(src_no_comments):
    method_name = m.group(1)
    body = m.group(2)

    # Page-load fixtures by method-name convention.
    if method_name.startswith('loads_') or method_name.endswith('_loads'):
        continue

    has_visibility = any(tok in body for tok in visibility_only_tokens)
    has_state = any(tok in body for tok in state_assertion_tokens)
    if has_visibility and not has_state:
        anti_methods.append(method_name)

if anti_methods:
    print(f'FAIL: {path}', file=sys.stderr)
    for am in anti_methods:
        print(f'  Anti-pattern method (visibility-only, no state assertion): {am}', file=sys.stderr)
    sys.exit(1)

sys.exit(0)
PYEOF
  rc=$?
  set -e
  if [[ $rc -ne 0 ]]; then
    VIOLATING_FILES+=("$file")
  fi
done < <(find "$TESTS_DIR" -name "*.cs" -not -name "*.Designer.cs" -print0)

if [[ ${#VIOLATING_FILES[@]} -gt 0 ]]; then
  echo "" >&2
  echo "FAIL: ${#VIOLATING_FILES[@]} test file(s) contain visibility-only assertions without state assertions." >&2
  echo "Per feedback_verify_ui_state_not_just_rendering.md: visible rows can hide silent rejection icons." >&2
  echo "Every non-PageLoad / non-[Explicit] test MUST include at least one state assertion (token list in this script)." >&2
  printf '  - %s\n' "${VIOLATING_FILES[@]}" >&2
  exit 1
fi

# ----------------------------------------------------------------------
# Gate 2 — GH #180 selector-strategy ban under Tests/Settings/
# ----------------------------------------------------------------------

SETTINGS_DIR="$TESTS_DIR/Settings"
SELECTOR_VIOLATIONS=()

if [[ -d "$SETTINGS_DIR" ]]; then
  while IFS= read -r -d '' file; do
    set +e
    python3 - "$file" <<'PYEOF'
import re, sys
path = sys.argv[1]
with open(path, encoding='utf-8') as f:
    src = f.read()

# Strip comments so an explanatory comment that mentions the banned shape
# (e.g. "replaces the prior `input[name='...']` fallback") doesn't false-
# positive. Line + block comments only.
src_no_comments = re.sub(r'^\s*//.*$', '', src, flags=re.MULTILINE)
src_no_comments = re.sub(r'/\*[\s\S]*?\*/', '', src_no_comments)

violations = []

# Ban 1: Locator("input[name=...")
# Matches: .Locator("input[name='foo']"), Page.Locator("input[name=\"bar\"]")
input_name_pat = re.compile(r'\.Locator\(\s*"input\[name=')
for m in input_name_pat.finditer(src_no_comments):
    # Compute 1-indexed line number for the match offset
    line_no = src_no_comments.count('\n', 0, m.start()) + 1
    violations.append((line_no, 'Locator("input[name=...")'))

# Ban 2: Locator("label:has(input...")
label_has_input_pat = re.compile(r'\.Locator\(\s*"label:has\(input')
for m in label_has_input_pat.finditer(src_no_comments):
    line_no = src_no_comments.count('\n', 0, m.start()) + 1
    violations.append((line_no, 'Locator("label:has(input...")'))

if violations:
    print(f'FAIL: {path}', file=sys.stderr)
    for line_no, shape in violations:
        print(f'  Line {line_no}: banned selector shape `{shape}` — use GetByTestId(...) per D-18.', file=sys.stderr)
    sys.exit(1)

sys.exit(0)
PYEOF
    rc=$?
    set -e
    if [[ $rc -ne 0 ]]; then
      SELECTOR_VIOLATIONS+=("$file")
    fi
  done < <(find "$SETTINGS_DIR" -name "*.cs" -not -name "*.Designer.cs" -print0)
fi

if [[ ${#SELECTOR_VIOLATIONS[@]} -gt 0 ]]; then
  echo "" >&2
  echo "FAIL: ${#SELECTOR_VIOLATIONS[@]} test file(s) under Tests/Settings/ bypass the D-18 testid contract." >&2
  echo "Per GH #180: Locator(\"input[name=...\") and Locator(\"label:has(input...\") are banned in Tests/Settings/." >&2
  echo "Use Page.GetByTestId(\"settings-{provider}-field-{name}\") for inputs and the per-row checkbox testid for row checkboxes." >&2
  printf '  - %s\n' "${SELECTOR_VIOLATIONS[@]}" >&2
  exit 1
fi

echo "PASS: No state-not-rendering anti-pattern detected in src/NzbDrone.Automation.Test/Tests/."
echo "PASS: No banned wrapper-bypass selector shapes detected in src/NzbDrone.Automation.Test/Tests/Settings/."
exit 0
