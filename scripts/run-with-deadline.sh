#!/usr/bin/env bash
# Usage: scripts/run-with-deadline.sh <seconds> <command> [args...]
#
# Runs the command in its own process group and ends the step by ITSELF, as a failed step, in two
# cases, so the steps after it (naming the hung test, uploading the log) still run:
#   * deadline: still running after <seconds>: print the process tree, SIGKILL the whole group, exit 1.
#   * memory:   MemAvailable below POLY_MEM_THRESHOLD_MB (default 400): print the reading and the ten
#     largest processes, SIGKILL the whole group, exit 1. 400 MB is about 2.5% of the 16 GB hosted
#     runner: small enough that a healthy run (page cache counts as available) never trips it, large
#     enough that the kill and the log flush still fit before the runner agent itself is starved.
# Otherwise it exits with the command's own exit code and kills stragglers left in the group.
# Why: a test host blocked on a synchronous wait ignores a polite stop and the runner's step timeout
# did not end it (PR #878); and four later hangs (#870) ended only at the SERVER's 20-minute job limit
# with no log: a starved runner enforces neither the step timeout nor a watchdog it has no memory
# to run. The memory loop uses shell builtins only in its fast path (no ps, no pipes).
# POLY_MEMINFO overrides /proc/meminfo (tests); when it does not exist the memory watchdog is off,
# the summary says "memory: unavailable" and the deadline still works.
# Always prints: nproc and MemTotal at the start, a heartbeat every 30 s, one `memory:` line at the end.
set -u
deadline=$1
shift
meminfo=${POLY_MEMINFO:-/proc/meminfo}
threshold_mb=${POLY_MEM_THRESHOLD_MB:-400}
flag=$(mktemp)
state=$(mktemp)
rm -f "$flag"

# Sets mem_total mem_avail swap_total swap_free (MB); returns 1 when meminfo is unusable.
read_mem() {
  local k v _
  mem_total= mem_avail= swap_total=0 swap_free=0
  [ -r "$meminfo" ] || return 1
  while read -r k v _; do
    case $k in
      MemTotal:) mem_total=$((v / 1024)) ;;
      MemAvailable:) mem_avail=$((v / 1024)) ;;
      SwapTotal:) swap_total=$((v / 1024)) ;;
      SwapFree:) swap_free=$((v / 1024)) ;;
    esac
  done < "$meminfo"
  [ -n "$mem_total" ] && [ -n "$mem_avail" ]
}

summary() {
  local total avail at swtotal swfree
  if read -r total avail at swtotal swfree < "$state" && [ -n "${avail:-}" ]; then
    echo "memory: MemTotal ${total}MB, lowest MemAvailable ${avail}MB at ${at}s, SwapTotal ${swtotal}MB, lowest SwapFree ${swfree}MB; cores $cores"
  else
    echo "memory: unavailable; cores $cores"
  fi
}

cores=$(nproc 2>/dev/null || sysctl -n hw.ncpu 2>/dev/null || echo '?')
if read_mem; then
  echo "cores $cores, MemTotal ${mem_total}MB, MemAvailable ${mem_avail}MB, threshold ${threshold_mb}MB"
else
  echo "cores $cores, memory: unavailable ($meminfo)"
fi

set -m # job control: each background job gets its own process group, pgid == its pid
watchdog=
memdog=
pid=
stop_helpers() {
  [ -n "$watchdog" ] && kill -KILL -- "-$watchdog" 2>/dev/null
  [ -n "$memdog" ] && kill -KILL -- "-$memdog" 2>/dev/null
  return 0
}
# A TERM/INT/HUP to this script (a cancelled job, the step's timeout) must not orphan the command's
# group or the watchdogs: they would live on holding the step's output pipe.
trap 'kill -KILL -- "-$pid" 2>/dev/null; stop_helpers; summary; exit 143' TERM INT HUP
"$@" &
pid=$!
(
  sleep "$deadline"
  echo deadline > "$flag"
  echo "::error::the test run was still going after ${deadline}s and is being killed (SIGKILL to its whole process group $pid)"
  echo "processes alive at the deadline:"
  { ps -eo pid,ppid,pgid,etime,args --forest 2>/dev/null || ps -eo pid,ppid,pgid,etime,command; } | cut -c1-200
  kill -KILL -- "-$pid" 2>/dev/null
) &
watchdog=$!
disown "$watchdog" # no job-status noise when it is killed

if read_mem; then
  (
    min_avail=$mem_avail min_at=0 min_swap=$swap_free n=0
    printf '%s %s %s %s %s\n' "$mem_total" "$min_avail" "$min_at" "$swap_total" "$min_swap" > "$state"
    while :; do
      sleep 5
      read_mem || continue
      n=$((n + 1))
      if [ "$mem_avail" -lt "$min_avail" ] || [ "$swap_free" -lt "$min_swap" ]; then
        [ "$mem_avail" -lt "$min_avail" ] && { min_avail=$mem_avail; min_at=$SECONDS; }
        [ "$swap_free" -lt "$min_swap" ] && min_swap=$swap_free
        printf '%s %s %s %s %s\n' "$mem_total" "$min_avail" "$min_at" "$swap_total" "$min_swap" > "$state"
      fi
      if [ $((n % 6)) -eq 0 ]; then
        load=
        [ -r /proc/loadavg ] && read -r load _ < /proc/loadavg
        echo "hb ${SECONDS}s avail=${mem_avail}MB swapfree=${swap_free}MB load=${load:-?}"
      fi
      if [ "$mem_avail" -lt "$threshold_mb" ]; then
        echo memory > "$flag"
        echo "::error::MemAvailable ${mem_avail}MB is below ${threshold_mb}MB after ${SECONDS}s (SwapFree ${swap_free}MB): killing the test run's process group $pid before the runner starves"
        echo "ten largest processes by RSS (KB):"
        { ps -eo rss,pid,ppid,pgid,etime,args --sort=-rss 2>/dev/null || ps -eo rss,pid,ppid,pgid,etime,command | sort -rn; } | head -11 | cut -c1-200
        echo "processes in the command's group: $(ps -eo pgid | awk -v g="$pid" '$1 == g' | wc -l)"
        kill -KILL -- "-$pid" 2>/dev/null
        exit 0
      fi
    done
  ) &
  memdog=$!
  disown "$memdog"
fi

wait "$pid"
rc=$?
stop_helpers
# Stragglers of a finished run still hold the output pipe: say what they are, then kill them.
left=$(ps -eo pid,pgid,args | awk -v g="$pid" '$2 == g { print }' | cut -c1-200)
if [ -n "$left" ]; then
  echo "::warning::processes still alive in the test command's group after it exited (killed now):"
  echo "$left"
fi
kill -KILL -- "-$pid" 2>/dev/null

summary
if [ -s "$flag" ]; then
  rm -f "$flag" "$state"
  exit 1
fi
rm -f "$flag" "$state"
exit "$rc"
