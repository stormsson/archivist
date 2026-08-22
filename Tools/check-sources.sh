#!/usr/bin/env bash
# A2, third clause (§13.2): source assertion over the Generation assembly.
# "No System.Random / UnityEngine.Random / GetHashCode in the Generation assembly."
set -u
GEN="$(cd "$(dirname "$0")/.." && pwd)/Assets/Archivist/Generation"
fail=0

# Comment lines are stripped before matching. The rules forbid these APIs in CODE;
# a comment that NAMES one ("never use UnityEngine.Random") is documentation, and
# failing on it trains people to delete the very comments that explain the rule.
# grep output is prefixed "path:line:", so the anchor must skip past that before
# looking for the comment marker.
strip_comments() { grep -vE '^[^:]+:[0-9]+:[[:space:]]*(//|\*|/\*)'; }

check() {  # name, pattern, extra grep args
  local name="$1"; shift
  local pat="$1"; shift
  local hits
  hits=$(grep -rnE "$pat" "$GEN" --include='*.cs' "$@" | strip_comments || true)
  if [ -n "$hits" ]; then
    echo "  FAIL  $name"
    echo "$hits" | sed 's|^|          |'
    fail=1
  else
    echo "  PASS  $name"
  fi
}

echo "Source assertions over Generation/ (§13.2, §4.1, §14)"
check "no System.Random"        '(^|[^.[:alnum:]_])(System\.)?Random[[:space:]]*\('
check "no UnityEngine.Random"   'UnityEngine\.Random|Random\.(value|Range|insideUnitCircle)'
# The §4.1 hazard is seeding from GetHashCode (string.GetHashCode is process-randomised).
# An `override int GetHashCode()` on our own value type is for dictionary lookup only and
# never drives generation, so that one declaration line is exempt.
hits=$(grep -rnE '\.GetHashCode\(\)' "$GEN" --include='*.cs' | strip_comments | grep -v 'override int GetHashCode' || true)
if [ -n "$hits" ]; then
  echo "  FAIL  no GetHashCode used for seeding"
  echo "$hits" | sed 's|^|          |'
  fail=1
else
  echo "  PASS  no GetHashCode used for seeding"
fi
check "no UnityEngine reference" '(^|[^[:alnum:]_])using[[:space:]]+UnityEngine'
check "no wall-clock"           'DateTime\.(Now|UtcNow)|Stopwatch|Environment\.TickCount|Time\.(time|deltaTime|frameCount)'
check "no file-scoped namespace" '^namespace[[:space:]]+[A-Za-z0-9_.]+[[:space:]]*;'
check "no records"              '^[[:space:]]*(public|internal)[[:space:]]+(sealed[[:space:]]+)?record[[:space:]]'

exit $fail
