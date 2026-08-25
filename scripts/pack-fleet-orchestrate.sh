#!/usr/bin/env bash
# Run pack-* waves in order. Wave A is assumed already launched unless --all.
# Usage: scripts/pack-fleet-orchestrate.sh [--from STEM]
set -euo pipefail
cd "$(dirname "$0")/.."
LOG=/tmp/poly-pack-fleet
mkdir -p "$LOG"

run_one() {
  local stem="$1"
  echo "== $(date -u +%H:%M:%SZ) launch $stem =="
  scripts/pack-fleet-agent.sh "$stem" > "$LOG/${stem}.log" 2>&1
  local ec=$?
  echo "== $(date -u +%H:%M:%SZ) $stem exit $ec =="
  return $ec
}

run_parallel() {
  local pids=()
  local stems=("$@")
  for s in "${stems[@]}"; do
    echo "== $(date -u +%H:%M:%SZ) launch $s =="
    scripts/pack-fleet-agent.sh "$s" > "$LOG/${s}.log" 2>&1 &
    pids+=($!)
  done
  local fail=0
  for i in "${!pids[@]}"; do
    if ! wait "${pids[$i]}"; then
      echo "== FAIL ${stems[$i]} =="
      fail=1
    else
      echo "== ok ${stems[$i]} =="
    fi
  done
  return $fail
}

# Wave A already started by coordinator if logs exist; wait for those jobs if pids given.
# This script starts from Wave B unless --all.

if [ "${1:-}" = "--all" ]; then
  run_parallel pack-1-1-token-writer pack-1-2-print-binder || exit 1
fi

run_one pack-1-3-dsl-printer || exit 1
run_one pack-1-4-e1-patterns || exit 1
run_one pack-1-gate || exit 1

run_one pack-2-1-idomainpack || exit 1
run_parallel pack-2-2-sqlite pack-2-3-sqlserver pack-2-4-mysql || exit 1
run_parallel pack-2-5-compiler pack-2-6-mcp || exit 1
run_one pack-2-gate || exit 1

# 3a: walk p1 tasks in order
for t in p1-0-inventory-ir p1-1-now-expression p1-2-duration-forms p1-3-pack-registration \
         p1-4-analysis-fail-closed p1-5-goldens p1-6-guide pack-3a-print-roundtrip p1-gate; do
  run_one "$t" || exit 1
done

run_one pack-3b-1-producer || exit 1
run_one pack-3b-2-session || exit 1
run_one pack-3b-3-roundtrip || exit 1
run_one pack-3b-gate || exit 1

run_one pack-3c-1-artifact-hook || exit 1
run_one pack-3c-2-minapi || exit 1
run_one pack-3c-3-bind-export || exit 1
run_one pack-3c-gate || exit 1

echo "== fleet complete =="
