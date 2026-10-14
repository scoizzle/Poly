#!/usr/bin/env bash
# Runs one probe domain through the automated discovery pipeline:
#   parse/analyze -> export C# -> Roslyn compile-check
# Usage: scripts/run-probe.sh docs/probes/<name>.poly
# Exits 0 when the export compiles with 0 errors. Warnings are printed and do not fail
# the gate (probe-check's contract). A domain that `uses http` is compiled
# --mode all --dbms sqlite --http and every emitted file is built as a host.
set -euo pipefail
cd "$(dirname "$0")/.."

PROBE="${1:?usage: scripts/run-probe.sh docs/probes/<name>.poly}"
[ -f "$PROBE" ] || { echo "probe not found: $PROBE" >&2; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

if grep -Eq '^[[:space:]]*uses[[:space:]]+http([[:space:]]|$)' "$PROBE"; then
  echo "== Exporting $PROBE (http: --mode all --dbms sqlite --http) =="
  mkdir -p "$TMP/out"
  dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- \
    --mode all --dbms sqlite --http "$PROBE" "$TMP/out"

  cat > "$TMP/out/ProbeHost.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
    <PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.13" />
  </ItemGroup>
</Project>
EOF

  echo "== Compile-check (every emitted file) =="
  dotnet build "$TMP/out/ProbeHost.csproj" --nologo -v q
  echo "errors: 0"
else
  echo "== Exporting $PROBE =="
  dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- "$PROBE" \
      | sed '/^\/\/ =====/d' \
      | awk 'BEGIN{d["using System;"]=1;d["using System.Collections.Generic;"]=1} !/^using / || !(seen[$0]++){print}' \
      > "$TMP/export.cs"

  echo "== Compile-check =="
  dotnet run --project scripts/probe-check/probe-check.csproj -- "$TMP/export.cs"
fi
