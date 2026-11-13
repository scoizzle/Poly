#!/usr/bin/env bash
# Usage: scripts/run-with-deadline.sh <seconds> <command> [args...]
#
# Runs the command in its own process group. If it is still running after <seconds>, prints the
# process tree and kills the WHOLE group with SIGKILL (the command and everything it spawned), then
# exits 1 with an ::error:: line. Otherwise exits with the command's own exit code, and any
# stragglers left in the group are killed so nothing keeps the step's output pipe open.
# Why: a test host blocked on a synchronous wait ignores a polite stop, and the runner's step
# timeout did not end it either (PR #878, run 43422550913): the job was cancelled and the steps that
# name the hung test never ran. A step that ends itself, failed, lets them run.
set -u
deadline=$1
shift
flag=$(mktemp)
rm -f "$flag"

set -m # job control: each background job gets its own process group, pgid == its pid
watchdog=
pid=
# A TERM/INT/HUP to this script (a cancelled job, the step's timeout) must not orphan the command's
# group or the watchdog: they would live on holding the step's output pipe.
trap 'kill -KILL -- "-$pid" 2>/dev/null; [ -n "$watchdog" ] && kill -KILL -- "-$watchdog" 2>/dev/null; exit 143' TERM INT HUP
"$@" &
pid=$!
(
  sleep "$deadline"
  : > "$flag"
  echo "::error::the test run was still going after ${deadline}s and is being killed (SIGKILL to its whole process group $pid)"
  echo "processes alive at the deadline:"
  { ps -eo pid,ppid,pgid,etime,args --forest 2>/dev/null || ps -eo pid,ppid,pgid,etime,command; } | cut -c1-200
  kill -KILL -- "-$pid" 2>/dev/null
) &
watchdog=$!

wait "$pid"
rc=$?
kill -KILL -- "-$watchdog" 2>/dev/null
wait "$watchdog" 2>/dev/null
# Stragglers of a finished run still hold the output pipe: say what they are, then kill them.
left=$(ps -eo pid,pgid,args | awk -v g="$pid" '$2 == g { print }' | cut -c1-200)
if [ -n "$left" ]; then
  echo "::warning::processes still alive in the test command's group after it exited (killed now):"
  echo "$left"
fi
kill -KILL -- "-$pid" 2>/dev/null

if [ -e "$flag" ]; then
  rm -f "$flag"
  exit 1
fi
exit "$rc"
