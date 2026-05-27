#! /bin/bash
PLATFORM=$1
TYPE=$2
COVERAGE=$3
WHERE="Category!=ManualTest&Category!=LiveComix"
TEST_PATTERN="*Test.dll"
FILES=( "Mangarr.Api.Test.dll" "Mangarr.Automation.Test.dll" "Mangarr.Common.Test.dll" "Mangarr.Core.Test.dll" "Mangarr.Host.Test.dll" "Mangarr.Integration.Test.dll" "Mangarr.Libraries.Test.dll" "Mangarr.Mono.Test.dll" "Mangarr.Update.Test.dll" "Mangarr.Windows.Test.dll" )
ASSMEBLIES=""
TEST_LOG_FILE="TestLog.txt"

echo "test dir: $TEST_DIR"
if [ -z "$TEST_DIR" ]; then
    TEST_DIR="."
fi

if [ -d "$TEST_DIR/_tests" ]; then
  TEST_DIR="$TEST_DIR/_tests"
fi

rm -f "$TEST_LOG_FILE"

# Uncomment to log test output to a file instead of the console
export MANGARR_TESTS_LOG_OUTPUT="File"

VSTEST_PARAMS="--logger:nunit;LogFilePath=TestResult.xml"

if [ "$PLATFORM" = "Mac" ]; then

  export DYLD_FALLBACK_LIBRARY_PATH="$TEST_DIR:$MONOPREFIX/lib:/usr/local/lib:/lib:/usr/lib"
  echo $DYLD_FALLBACK_LIBRARY_PATH
  mono --version

  # To debug which libraries are being loaded:
  # export DYLD_PRINT_LIBRARIES=YES
fi

if [ "$PLATFORM" = "Windows" ]; then
  mkdir -p "$ProgramData/Mangarr"
  WHERE="$WHERE&Category!=LINUX"
elif [ "$PLATFORM" = "Linux" ]; then
  mkdir -p ~/.config/Mangarr
  WHERE="$WHERE&Category!=WINDOWS"
elif  [ "$PLATFORM" = "Mac" ]; then
  mkdir -p ~/Library/Application\ Support/Mangarr
  WHERE="$WHERE&Category!=WINDOWS"
else
  echo "Platform must be provided as first argument: Windows, Linux or Mac"
  exit 1
fi

if [ "$TYPE" = "Unit" ]; then
  WHERE="$WHERE&Category!=IntegrationTest&Category!=AutomationTest"
elif [ "$TYPE" = "Integration" ] || [ "$TYPE" = "int" ] ; then
  WHERE="$WHERE&Category=IntegrationTest"
elif [ "$TYPE" = "Automation" ] ; then
  WHERE="$WHERE&Category=AutomationTest"

  # Offline-by-default for the automation tier. The Comix-enabled fixtures
  # (InteractiveSearch{Modal,Grab,Open}Fixture, which set
  # DisableComixIndexerInBaseline => false) fan out through IComixSigner. Without
  # MANGARR_TEST_CASSETTE_MODE set, NzbDrone.Host/Startup.cs resolves the LIVE
  # ComixPlaywrightSigner, which launches Chromium against comix.to (and likewise
  # the MangaDex HTTP cassette layer would hit api.mangadex.org). Default to Replay
  # against the committed cassettes so a local `test.sh ... Automation ...` run is
  # hermetic, matching the CI automation jobs in .github/workflows/build_v5.yml.
  # Export an explicit MANGARR_TEST_CASSETTE_MODE=Record|ReplayOrRecord before
  # invoking to (re)record. Live-network coverage lives in the [Category=LiveService]
  # tier, which is excluded from this offline run.
  CASSETTE_REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/.." && pwd)"
  CASSETTE_DEFAULT_DIR="$CASSETTE_REPO_ROOT/src/NzbDrone.Automation.Test/Fixtures/Cassettes"
  # The child Mangarr is a native .NET process. Under Git Bash on Windows `pwd`
  # yields an MSYS path (/c/Users/...) that .NET can't resolve, so translate to a
  # Windows path (C:\Users\...) via cygpath. No-op on Linux/Mac (pwd is native).
  if command -v cygpath >/dev/null 2>&1; then
    CASSETTE_DEFAULT_DIR="$(cygpath -w "$CASSETTE_DEFAULT_DIR")"
  fi
  : "${MANGARR_TEST_CASSETTE_MODE:=Replay}"
  : "${MANGARR_TEST_CASSETTE_DIR:=$CASSETTE_DEFAULT_DIR}"
  export MANGARR_TEST_CASSETTE_MODE MANGARR_TEST_CASSETTE_DIR
  echo "Automation cassette mode: $MANGARR_TEST_CASSETTE_MODE (dir: $MANGARR_TEST_CASSETTE_DIR)"
else
  echo "Type must be provided as second argument: Unit, Integration or Automation"
  exit 2
fi

for i in "${FILES[@]}";
  do ASSEMBLIES="$ASSEMBLIES $TEST_DIR/$i"
done

DOTNET_PARAMS="$ASSEMBLIES --filter:$WHERE $VSTEST_PARAMS"

if [ "$COVERAGE" = "Coverage" ]; then
  dotnet test $DOTNET_PARAMS --settings:"src/coverlet.runsettings" --results-directory:./CoverageResults
  EXIT_CODE=$?
elif [ "$COVERAGE" = "Test" ] ; then
  dotnet test $DOTNET_PARAMS
  EXIT_CODE=$?
else
  echo "Run Type must be provided as third argument: Coverage or Test"
  exit 3
fi

if [ "$EXIT_CODE" -ge 0 ]; then
  echo "Failed tests: $EXIT_CODE"
  exit 0
else
  exit $EXIT_CODE
fi
