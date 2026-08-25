#!/usr/bin/env bash
# Launch one pack-* fleet agent via opencode CLI.
# Usage: scripts/pack-fleet-agent.sh <task-id>
#   task-id e.g. pack-1-1-token-writer  (file stem under docs/plans/simple-agent-tasks/)
set -euo pipefail
cd "$(dirname "$0")/.."

STEM="${1:?usage: scripts/pack-fleet-agent.sh <task-stem>}"
TASK="docs/plans/simple-agent-tasks/${STEM}.md"
if [ ! -f "$TASK" ]; then
  echo "missing $TASK" >&2
  exit 1
fi

exec opencode run \
  --dir . \
  --auto \
  --agent build \
  --title "$STEM" \
  "You are fleet agent ${STEM} in the Poly repo.
Read AGENTS.md, docs/CORE.md, docs/plans/simple-agent-tasks/pack-README.md, and ${TASK}.
Write 'Claimed by: opencode (${STEM})' on the task file BEFORE any code edit.
Follow Exact steps. File ownership is exclusive — do not edit Do-not-edit files.
Write one failing TUnit test first, then the smallest production fix.
Do not add IExpressionPrintForm. Do not invent DslLayout. Do not add an import keyword.
Do not re-add Link/Unlink/Delete effects. Do not extract DomainToCSharpExporter.
Do not start a later pack-* task.
Verify with the task's filter, then:
  dotnet build Poly.Benchmarks/Poly.Benchmarks.csproj
  dotnet run --project Poly.Tests/Poly.Tests.csproj
Mark the task Status [x] and the slice README table. Stop when the assigned task is Done."
