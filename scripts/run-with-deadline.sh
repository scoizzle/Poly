#!/usr/bin/env bash
# Usage: scripts/run-with-deadline.sh <seconds> <command> [args...]
#
# Runs the command in its own process group and ends the step by ITSELF, as a failed step, so the
# steps after it (naming the hung test, uploading the log) still run:
#   * deadline: still running after <seconds>: print the process tree, SIGKILL the whole group, exit 1.
#   * memory:   sampled every 2 s from /proc/meminfo; SIGKILL the whole group, exit 1, when
#       - SwapFree < POLY_SWAP_FLOOR_MB (default 1000; off when the machine has no swap), or
#       - MemAvailable < POLY_MEM_THRESHOLD_MB (default 200) on TWO consecutive samples.
#     A one-time ::warning:: (with the process snapshot) when MemAvailable first drops below 600 MB.
#     These numbers come from ONE measurement (a green 4-core, 16 GB hosted run, PR #892: available
#     memory fell to a sampled 560 MB at 220 s with about 300 MB swings per sample, and swap was first
#     touched near 900 MB; the suite is otherwise healthy there). They are judgement, tunable by the
#     two variables above, and a healthy run may still come near them.
# Otherwise it exits with the command's own exit code and kills stragglers left in the group.
# Why (HYPOTHESIS, #870): the runner is starved of memory, so the step timeout and any watchdog that
# needs a fork or an allocation stop being enforced and only the SERVER's 20-minute job limit ends the
# job, with no log. The measurement above supports it; it is not proven to be the cause of the hangs.
# The memory loop therefore uses shell builtins only (no ps, no pipes, no forks except sleep); the
# kill path reads /proc directly. POLY_MEMINFO and POLY_PROC override /proc/meminfo and /proc (tests);
# without a meminfo file the memory watchdog is off, the summary says "memory: unavailable" and the
# deadline still works. The snapshot assumes 4 KB pages (statm is in pages).
# Always prints: cores and MemTotal at the start, a heartbeat every 30 s (memory, load, memory
# pressure, and the snapshot: sum of RSS, the command's group, the three largest processes), and one
# `memory:` line at the end.
set -u
deadline=$1
shift
meminfo=${POLY_MEMINFO:-/proc/meminfo}
proc=${POLY_PROC:-/proc}
threshold_mb=${POLY_MEM_THRESHOLD_MB:-200}
swap_floor_mb=${POLY_SWAP_FLOOR_MB:-1000}
warn_mb=600
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

# Builtins only (no fork). Sets $snapshot from $proc: statm field 2 = resident pages; in stat, after
# the ")" that ends the command name, field 3 of the rest is the process group.
take_snapshot() {
  local d p r c s tot=0 gm=0 gn=0 v1=0 v2=0 v3=0 l1= l2= l3=
  for d in "$proc"/[0-9]*; do
    p=${d##*/}
    read -r _ r _ 2> /dev/null < "$d/statm" || continue
    read -r c 2> /dev/null < "$d/comm" || c=?
    r=$((r * 4 / 1024))
    tot=$((tot + r))
    if [ "$r" -gt "$v1" ]; then
      v3=$v2 l3=$l2 v2=$v1 l2=$l1 v1=$r l1="$c($p)"
    elif [ "$r" -gt "$v2" ]; then
      v3=$v2 l3=$l2 v2=$r l2="$c($p)"
    elif [ "$r" -gt "$v3" ]; then
      v3=$r l3="$c($p)"
    fi
    read -r s 2> /dev/null < "$d/stat" || continue
    set -- ${s##*) }
    if [ "${3:-}" = "$pid" ]; then
      gm=$((gm + r))
      gn=$((gn + 1))
    fi
  done
  snapshot="rss_sum=${tot}MB group=${gn}procs/${gm}MB top=${l1}/${v1}MB,${l2}/${v2}MB,${l3}/${v3}MB"
}

summary() {
  local total avail at swtotal swfree
  if read -r total avail at swtotal swfree < "$state" && [ -n "${avail:-}" ]; then
    echo "memory: MemTotal ${total}MB, lowest MemAvailable ${avail}MB at ${at}s, SwapTotal ${swtotal}MB, lowest SwapFree ${swfree}MB; cores $cores"
  else
    echo "memory: unavailable; cores $cores"
  fi
}

cores=$(nproc 2> /dev/null || sysctl -n hw.ncpu 2> /dev/null || echo '?')
if read_mem; then
  echo "cores $cores, MemTotal ${mem_total}MB, MemAvailable ${mem_avail}MB, kill below ${threshold_mb}MB twice running or SwapFree below ${swap_floor_mb}MB"
else
  echo "cores $cores, memory: unavailable ($meminfo)"
fi

set -m # job control: each background job gets its own process group, pgid == its pid
watchdog=
memdog=
pid=
stop_helpers() {
  [ -n "$watchdog" ] && kill -KILL -- "-$watchdog" 2> /dev/null
  [ -n "$memdog" ] && kill -KILL -- "-$memdog" 2> /dev/null
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
  { ps -eo pid,ppid,pgid,etime,args --forest 2> /dev/null || ps -eo pid,ppid,pgid,etime,command; } | cut -c1-200
  kill -KILL -- "-$pid" 2> /dev/null
) &
watchdog=$!
disown "$watchdog" # no job-status noise when it is killed

if read_mem; then
  (
    min_avail=$mem_avail min_at=0 min_swap=$swap_free n=0 low=0 warned=0
    printf '%s %s %s %s %s\n' "$mem_total" "$min_avail" "$min_at" "$swap_total" "$min_swap" > "$state"
    while :; do
      sleep 2
      read_mem || continue
      n=$((n + 1))
      if [ "$mem_avail" -lt "$min_avail" ] || [ "$swap_free" -lt "$min_swap" ]; then
        [ "$mem_avail" -lt "$min_avail" ] && { min_avail=$mem_avail; min_at=$SECONDS; }
        [ "$swap_free" -lt "$min_swap" ] && min_swap=$swap_free
        printf '%s %s %s %s %s\n' "$mem_total" "$min_avail" "$min_at" "$swap_total" "$min_swap" > "$state"
      fi
      if [ "$mem_avail" -lt "$threshold_mb" ]; then low=$((low + 1)); else low=0; fi
      reason=
      if [ "$swap_total" -gt 0 ] && [ "$swap_free" -lt "$swap_floor_mb" ]; then
        reason="SwapFree ${swap_free}MB is below ${swap_floor_mb}MB (more than $((swap_total - swap_floor_mb))MB swapped out)"
      elif [ "$low" -ge 2 ]; then
        reason="MemAvailable ${mem_avail}MB is below ${threshold_mb}MB on two consecutive samples"
      fi
      if [ -n "$reason" ]; then
        echo memory > "$flag"
        echo "::error::$reason after ${SECONDS}s: killing the test run's process group $pid before the runner starves"
        take_snapshot
        echo "snapshot: $snapshot"
        kill -KILL -- "-$pid" 2> /dev/null
        read_mem
        echo "memory-after-kill: avail=${mem_avail}MB swapfree=${swap_free}MB"
        exit 0
      fi
      if [ "$warned" -eq 0 ] && [ "$mem_avail" -lt "$warn_mb" ]; then
        warned=1
        take_snapshot
        echo "::warning::MemAvailable ${mem_avail}MB is below ${warn_mb}MB after ${SECONDS}s (not killed; SwapFree ${swap_free}MB); snapshot: $snapshot"
      fi
      if [ $((n % 15)) -eq 0 ]; then
        load= a= psi=
        [ -r /proc/loadavg ] && read -r load _ < /proc/loadavg
        if [ -r "$proc/pressure/memory" ]; then
          read -r _ a _ < "$proc/pressure/memory"
          psi=" psi=${a#avg10=}"
        fi
        take_snapshot
        echo "hb ${SECONDS}s avail=${mem_avail}MB swapfree=${swap_free}MB load=${load:-?}${psi} $snapshot"
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
kill -KILL -- "-$pid" 2> /dev/null

summary
if [ -s "$flag" ]; then
  rm -f "$flag" "$state"
  exit 1
fi
rm -f "$flag" "$state"
exit "$rc"
