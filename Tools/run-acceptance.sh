#!/usr/bin/env bash
# Integration build + headless §13 acceptance run.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET=/usr/local/share/dotnet/x64/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

echo "== build =="
"$DOTNET" build "$ROOT/Tools/GenHarness/GenHarness.csproj" -v q --nologo 2>&1 | grep -Ev '^$' | tail -40
status=${PIPESTATUS[0]}
if [ "$status" != "0" ]; then echo "BUILD FAILED"; exit 1; fi

echo
echo "== source assertions =="
"$ROOT/Tools/check-sources.sh"

echo
echo "== editor/tests type check =="
# The build above covers Generation + Render only (§11). This is the only thing short of
# opening Unity that looks at Editor/ and Tests/. It self-skips when no editor is installed.
"$ROOT/Tools/check-editor.sh"

echo
echo "== acceptance =="
"$DOTNET" run --project "$ROOT/Tools/GenHarness/GenHarness.csproj" --no-build -- "${1:-all}"
