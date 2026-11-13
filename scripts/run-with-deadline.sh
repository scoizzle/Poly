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
kill -KILL -- "-$pid" 2>/dev/null # stragglers of a finished run still hold the output pipe

if [ -e "$flag" ]; then
  rm -f "$flag"
  exit 1
fi
exit "$rc"
