#!/usr/bin/env bash
# Integration build + headless acceptance run, in named sections.
#
# The whole thing is ~3 minutes and almost all of that is island generation (~0.5 s each,
# see A8), so running everything to look at one check was the normal case and it should not
# have been. Every section is now selectable by name:
#
#   Tools/run-acceptance.sh                 build + sources + editor + every gated check
#   Tools/run-acceptance.sh --list          what the sections are and what they cost
#   Tools/run-acceptance.sh A8              one check, ~6 s, no Unity type check
#   Tools/run-acceptance.sh render          the POC-02 gates only, ~4 s
#   Tools/run-acceptance.sh gate -A2 -C2    the gates without the two 100x determinism loops
#   Tools/run-acceptance.sh all             everything, metrics and PNG sweep included
#   Tools/run-acceptance.sh sources         the source assertions alone, no build
#
# Sections owned by this script: `sources` (check-sources.sh) and `editor` (check-editor.sh).
# Everything else is a harness selector and is passed straight through -- see `--list`.
# Prefix any section with `-` to subtract it from what came before.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
DOTNET=/usr/local/share/dotnet/x64/dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# --- split the command line ---------------------------------------------------------
# `sources` and `editor` are handled here; everything else the harness understands, so it
# is forwarded verbatim rather than being re-listed (and re-forgotten) in this file.
HARNESS_ARGS=()
LIST=0
WANT_SOURCES=-1        # -1 = not mentioned, 0 = subtracted, 1 = asked for
WANT_EDITOR=-1
SAW_SELECTOR=0         # a positional selector, i.e. not an --option

for a in "$@"; do
  case "$a" in
    sources)   WANT_SOURCES=1;  SAW_SELECTOR=1 ;;
    -sources)  WANT_SOURCES=0 ;;
    editor)    WANT_EDITOR=1;   SAW_SELECTOR=1 ;;
    -editor)   WANT_EDITOR=0 ;;
    --list|-l) LIST=1 ;;
    --help|-h) sed -n '3,19p' "$0" | sed 's/^# \{0,1\}//'
               "$DOTNET" run --project "$ROOT/Tools/GenHarness/GenHarness.csproj" -v q -- --help
               exit 0 ;;
    all|gate)  SAW_SELECTOR=1
               [ "$WANT_SOURCES" = -1 ] && WANT_SOURCES=1
               [ "$WANT_EDITOR"  = -1 ] && WANT_EDITOR=1
               HARNESS_ARGS+=("$a") ;;
    -*)        HARNESS_ARGS+=("$a") ;;          # an option, or a subtracted harness selector
    *)         SAW_SELECTOR=1; HARNESS_ARGS+=("$a") ;;
  esac
done

# Nothing named at all means the default run: both local sections plus the harness's own
# default (`gate`). Naming specific checks means those checks and nothing else -- when you
# ask for A8 you are iterating on A8, and a 3 s Unity type check is not what you asked for.
if [ "$SAW_SELECTOR" = 0 ]; then
  [ "$WANT_SOURCES" = -1 ] && WANT_SOURCES=1
  [ "$WANT_EDITOR"  = -1 ] && WANT_EDITOR=1
fi
[ "$WANT_SOURCES" = -1 ] && WANT_SOURCES=0
[ "$WANT_EDITOR"  = -1 ] && WANT_EDITOR=0

# Does the harness have anything to do? Only if it was given a selector of its own, or if
# nothing was named at all. `Tools/run-acceptance.sh sources` must not pay for the build.
RUN_HARNESS=0
[ ${#HARNESS_ARGS[@]} -gt 0 ] && RUN_HARNESS=1
[ "$SAW_SELECTOR" = 0 ] && RUN_HARNESS=1

if [ "$LIST" = 1 ]; then
  echo "Sections owned by run-acceptance.sh:"
  echo "  sources   source assertions over Generation/ (§13.2, §4.1, §14)   ~0.2 s"
  echo "  editor    Generation + Render + Editor + Tests type check         ~3 s"
  echo
  "$DOTNET" run --project "$ROOT/Tools/GenHarness/GenHarness.csproj" -v q -- --list
  exit 0
fi

fail=0

if [ "$WANT_SOURCES" = 1 ]; then
  echo "== source assertions =="
  "$ROOT/Tools/check-sources.sh" || fail=1
  echo
fi

if [ "$WANT_EDITOR" = 1 ]; then
  echo "== editor/tests type check =="
  # The harness build below covers Generation + Render only (§11). This is the only thing
  # short of opening Unity that looks at Editor/ and Tests/. It self-skips when no editor
  # is installed.
  "$ROOT/Tools/check-editor.sh" || fail=1
  echo
fi

if [ "$RUN_HARNESS" = 1 ]; then
  echo "== build =="
  "$DOTNET" build "$ROOT/Tools/GenHarness/GenHarness.csproj" -v q --nologo 2>&1 | grep -Ev '^$' | tail -40
  status=${PIPESTATUS[0]}
  if [ "$status" != "0" ]; then echo "BUILD FAILED"; exit 1; fi

  echo
  echo "== acceptance =="
  # Split on emptiness: under `set -u`, expanding an empty array is an error in the bash 3.2
  # that ships with macOS, and passing no selector is exactly the default run.
  if [ ${#HARNESS_ARGS[@]} -gt 0 ]; then
    "$DOTNET" run --project "$ROOT/Tools/GenHarness/GenHarness.csproj" --no-build -- "${HARNESS_ARGS[@]}" || fail=1
  else
    "$DOTNET" run --project "$ROOT/Tools/GenHarness/GenHarness.csproj" --no-build || fail=1
  fi
fi

exit $fail
