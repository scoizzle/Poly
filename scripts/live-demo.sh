#!/usr/bin/env bash
# Compile a .poly to a runnable host and walk one HTTP path.
# Default: warehouse (create root + register child).
# Usage: scripts/live-demo.sh [path/to/domain.poly]
set -euo pipefail
cd "$(dirname "$0")/.."

POLY="${1:-docs/probes/fleet-eval/09-transport/warehouse.poly}"
[ -f "$POLY" ] || { echo "not found: $POLY" >&2; exit 1; }

OUT="${TMPDIR:-/tmp}/poly-live-demo"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "== compile $POLY =="
dotnet run --project src/Poly.DslCompiler/Poly.DslCompiler.csproj -c Release -- \
  --mode all --dbms sqlite "$POLY" "$OUT"

cat > "$OUT/Demo.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <NoWarn>$(NoWarn);CS8618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.10" />
  </ItemGroup>
</Project>
EOF

echo "== build host =="
dotnet build "$OUT/Demo.csproj" --nologo -v q

PORT="${PORT:-5201}"
URL="http://127.0.0.1:${PORT}"
rm -f "$OUT"/fleet.db "$OUT"/*.db

echo "== run + walk =="
# Pairing sessions use scripts/serve-poly.sh (foreground host, no walk).
dotnet run --project "$OUT/Demo.csproj" --no-build --urls "$URL" >"$OUT/server.log" 2>&1 &
PID=$!
cleanup() { kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true; }
trap cleanup EXIT

for i in $(seq 1 40); do
  if curl -sf "$URL/api/warehouses" >/dev/null 2>&1; then
    break
  fi
  sleep 0.25
done

curl -sf -X POST "$URL/api/warehouses" \
  -H 'Content-Type: application/json' \
  -d '{"Capacity":100,"Code":"WH-001","Name":"Main Depot","Zip":"98101"}' \
  | tee "$OUT/create.json"
echo
grep -q 'WH-001' "$OUT/create.json"

curl -sf "$URL/api/warehouses/WH-001" | tee "$OUT/get.json"
echo
grep -q 'WH-001' "$OUT/get.json"

VIN="1HGCM82633A004352"
curl -sf -X POST "$URL/api/warehouses/WH-001/registertruck" \
  -H 'Content-Type: application/json' \
  -d "{\"vin\":\"$VIN\",\"maxLoad\":8000}" \
  | tee "$OUT/truck.json"
echo
grep -q "$VIN" "$OUT/truck.json"

echo "== live demo OK =="
