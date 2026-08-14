#!/usr/bin/env bash
# Compile a .poly to a runnable Minimal API host and start it.
# Usage:
#   scripts/serve-poly.sh <domain.poly> [--port 5201] [--out DIR]
# Foreground: Ctrl-C stops the host.
set -euo pipefail
cd "$(dirname "$0")/.."

POLY=""
PORT="${PORT:-5201}"
OUT=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --port) PORT="$2"; shift 2 ;;
    --out) OUT="$2"; shift 2 ;;
    -*) echo "unknown flag: $1" >&2; exit 1 ;;
    *) POLY="$1"; shift ;;
  esac
done

[ -n "$POLY" ] && [ -f "$POLY" ] || { echo "usage: scripts/serve-poly.sh <domain.poly> [--port 5201] [--out DIR]" >&2; exit 1; }

OUT="${OUT:-${TMPDIR:-/tmp}/poly-serve}"
rm -rf "$OUT"
mkdir -p "$OUT"

echo "== compile $POLY → $OUT =="
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

echo "== build =="
dotnet build "$OUT/Demo.csproj" --nologo -v q

URL="http://127.0.0.1:${PORT}"
echo "== listen $URL =="
echo "    generated: $OUT"
echo "    demo.http: $OUT/demo.http  (sample bodies may violate constraints — use valid values)"
exec dotnet run --project "$OUT/Demo.csproj" --no-build --urls "$URL"
