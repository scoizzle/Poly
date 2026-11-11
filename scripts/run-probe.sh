#!/usr/bin/env bash
# Runs one probe domain through the automated discovery pipeline:
#   parse/analyze -> export C# -> Roslyn compile-check
# Usage: scripts/run-probe.sh docs/probes/<name>.poly
# Exits 0 when the export compiles with 0 errors and 0 warnings: a generated
# warning is an export bug, so it fails the gate. A domain that `uses http` is
# compiled and every emitted file is built as a host.
# The non-http path (probe-check) is a bare Roslyn compile without analyzers, so it
# fails on compiler warnings only; CA* warnings appear only on the http dotnet build (#469).
set -euo pipefail
cd "$(dirname "$0")/.."
REPO="$(pwd -P)"

PROBE="${1:?usage: scripts/run-probe.sh docs/probes/<name>.poly}"
[ -f "$PROBE" ] || { echo "probe not found: $PROBE" >&2; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

# polyc seeds only the doors a document lists, so the http branch supplies the
# storage and sqlite doors the old --mode all --dbms sqlite flags seeded.
prepare_http_probe() {
  local src="$1" dst="$2" need_storage=0 need_vendor=0
  grep -Eq '^[[:space:]]*uses[[:space:]]+storage([[:space:]]|$)' "$src" || need_storage=1
  grep -Eq '^[[:space:]]*uses[[:space:]]+(sqlite|sqlserver|mysql)([[:space:]]|$)' "$src" || need_vendor=1
  awk -v storage="$need_storage" -v vendor="$need_vendor" '
    { lines[NR] = $0 }
    END {
      at = 0
      for (i = 1; i <= NR; i++)
        if (lines[i] ~ /^[[:space:]]*uses[[:space:]]+/) at = i
      if (at == 0)
        for (i = 1; i <= NR; i++)
          if (lines[i] ~ /^[[:space:]]*domain[[:space:]]/) { at = i; break }
      if (at == 0) at = 1
      for (i = 1; i <= NR; i++) {
        print lines[i]
        if (i == at) {
          if (storage == 1) print "uses storage"
          if (vendor == 1) print "uses sqlite"
        }
      }
    }' "$src" > "$dst"
}

if grep -Eq '^[[:space:]]*uses[[:space:]]+http([[:space:]]|$)' "$PROBE"; then
  echo "== Exporting $PROBE (http host) =="
  prepare_http_probe "$PROBE" "$TMP/probe.poly"
  dotnet run --project src/polyc/polyc.csproj -c Release -- build "$TMP/probe.poly" --out "$TMP/out"

  OUTDIR="$(find "$TMP/out" -mindepth 1 -maxdepth 1 -type d -print -quit)"
  [ -n "$OUTDIR" ] || { echo "no export directory produced" >&2; exit 1; }

  # The emitted host calls the runtime storage port, so it references the Poly
  # library the same way a consumer of the export would.
  cat > "$OUTDIR/ProbeHost.csproj" <<EOF
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
    <PackageReference Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.13" />
    <ProjectReference Include="$REPO/Poly/Poly.csproj" />
  </ItemGroup>
</Project>
EOF

  echo "== Compile-check (every emitted file) =="
  dotnet build "$OUTDIR/ProbeHost.csproj" --nologo -v q
  echo "errors: 0, warnings: 0"
else
  echo "== Exporting $PROBE =="
  dotnet run --project src/polyc/polyc.csproj -c Release -- build "$PROBE" --out "$TMP/out"

  FILES=()
  while IFS= read -r file; do FILES+=("$file"); done < <(find "$TMP/out" -name '*.cs' | sort)
  [ "${#FILES[@]}" -gt 0 ] || { echo "no .cs files produced" >&2; exit 1; }

  echo "== Compile-check =="
  dotnet run --project scripts/probe-check/probe-check.csproj -- "${FILES[@]}"
fi
