#!/usr/bin/env bash
# Starts a discovery round: scaffolds the round's findings dir, runs a baseline
# probe sweep (every probe through run-probe.sh — parse → export → Roslyn
# compile-check), writes probes/findings/<round>/baseline.md, and prints the next
# steps. Agent LAUNCH itself is an opencode coordinator action (Task tool) — this
# script does the mechanical prep and ground-truth sweep.
#
# Usage: scripts/discovery-round.sh <round-name> [probe-glob ...]
#   probe-glob defaults to probes/discovery-*/*.poly and probes/*.poly
set -euo pipefail
cd "$(dirname "$0")/.."

ROUND="${1:?usage: scripts/discovery-round.sh <round-name> [probe-glob ...]}"
FIND="probes/findings/$ROUND"
mkdir -p "$FIND"

if [ $# -gt 1 ]; then
  PROBES=()
  for g in "${@:2}"; do PROBES+=($g); done
else
  PROBES=(probes/discovery-*/*.poly probes/*.poly)
fi

BASELINE="$FIND/baseline.md"
{
  echo "# Round '$ROUND' — probe baseline sweep"
  echo
  echo "Run: $(date -u +%Y-%m-%dT%H:%MZ) — every probe through scripts/run-probe.sh"
  echo "(parse → export → Roslyn compile-check, 0 errors/0 warnings gate)."
  echo
  echo "| probe | result | status |"
  echo "|-------|--------|--------|"
} > "$BASELINE"

pass=0; fail=0
for p in "${PROBES[@]}"; do
  [ -f "$p" ] || continue
  line=$(scripts/run-probe.sh "$p" 2>&1 | grep -E "^errors:" | tail -1 || true)
  if [ -z "$line" ]; then
    line="errors: ?"
    status="FAIL(no-result)"
  else
    if printf '%s' "$line" | grep -q "^errors: 0"; then
      status="PASS"
      pass=$((pass + 1))
    else
      status="FAIL"
      fail=$((fail + 1))
    fi
  fi
  echo "| \`$p\` | $line | $status |" >> "$BASELINE"
done

{
  echo
  echo "Sweep: $pass pass, $fail fail."
  echo "Failing probes are this round's compile-fail targets. Agents should ALSO hunt"
  echo "non-compile findings (export/runtime divergence, silent gaps, guide drift) per"
  echo "docs/agent/poly-discovery-loop.md."
} >> "$BASELINE"

echo "== Baseline written: $BASELINE =="
cat "$BASELINE"
echo
echo "== Next: launch agents per the 'Round coordinator' section of"
echo "   docs/agent/poly-discovery-loop.md (findings dir: probes/findings/$ROUND) =="
