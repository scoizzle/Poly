#!/usr/bin/env bash
# Rebuild Poly.Mcp and kill the running instance so opencode relaunches the
# poly-local MCP with the new build. Closes the dogfood loop end-to-end:
# fix -> test -> restart -> verify via MCP (no manual rebuild/restart step).
#
# Usage: scripts/restart-poly-mcp.sh
# After it finishes, the next poly-local MCP tool call reconnects (opencode may
# need a one-time /mcp reload in the TUI if it does not auto-reconnect).
set -euo pipefail

cd "$(dirname "$0")/.."

echo "== Building Poly.Mcp =="
dotnet build Poly.Mcp/Poly.Mcp.csproj -v q

echo "== Killing running Poly.Mcp processes =="
# opencode-launched parent (`dotnet run --project ./Poly.Mcp/...`)
pkill -9 -f 'dotnet run --project .*Poly.Mcp' 2>/dev/null || true
# The compiled server binary — tight pattern so it also catches orphaned
# instances left over from earlier sessions (PPID 1, spinning).
pkill -9 -f 'Poly.Mcp/bin' 2>/dev/null || true

sleep 1

if pgrep -f 'Poly.Mcp/bin' >/dev/null 2>&1; then
    echo "Warning: Poly.Mcp is still running; kill it manually before the next restart." >&2
    exit 1
fi

echo "== Done. Reconnect the poly-local MCP via /mcp in the opencode TUI (opencode does not auto-restart a killed local MCP mid-session). =="
