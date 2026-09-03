#!/usr/bin/env bash
# Runs one probe domain through the automated discovery pipeline:
#   parse/analyze -> export C# -> Roslyn compile-check
# Usage: scripts/run-probe.sh docs/probes/<name>.poly
# Exits 0 only when the export compiles with 0 errors / 0 warnings.
set -euo pipefail
cd "$(dirname "$0")/.."

PROBE="${1:?usage: scripts/run-probe.sh docs/probes/<name>.poly}"
[ -f "$PROBE" ] || { echo "probe not found: $PROBE" >&2; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "== Exporting $PROBE =="
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- "$PROBE" \
    | sed '/^\/\/ =====/d' \
    | awk 'BEGIN{d["using System;"]=1;d["using System.Collections.Generic;"]=1} !/^using / || !(seen[$0]++){print}' \
    > "$TMP/export.cs"

echo "== Compile-check =="
dotnet run --project scripts/probe-check/probe-check.csproj -- "$TMP/export.cs"
