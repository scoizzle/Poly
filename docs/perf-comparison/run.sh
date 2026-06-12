#!/bin/bash
# Performance comparison: Sieve of Eratosthenes across languages
# Uses Docker for consistent environments across platforms.
# C# native and Poly VM run from the host (no Docker needed).
set -e

cd "$(dirname "$0")"
RESULTS_DIR="./results"
mkdir -p "$RESULTS_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS="$RESULTS_DIR/results_$TIMESTAMP.csv"
LIMIT="${1:-1000000}"

echo "language,limit,primes,time_ms" | tee "$RESULTS"

run_docker() {
    local label="$1"
    local dockerfile="$2"
    echo "--- $label ---" >&2
    echo "  Building $dockerfile ..." >&2
    local output
    output=$(docker compose run --rm "$dockerfile" "$LIMIT" 2>/dev/null)
    if [ $? -ne 0 ]; then
        echo "  FAILED" >&2
        return
    fi
    echo "$output" | tee -a "$RESULTS"
}

# ── Docker-based platforms ──
run_docker "C"       sieve-c
run_docker "C++"     sieve-cpp
run_docker "Rust"    sieve-rust
run_docker "Python"  sieve-python
run_docker "JS"      sieve-js
run_docker "Bun"     sieve-bun
run_docker "Deno"    sieve-deno
run_docker "Py+NumPy" sieve-numpy

# ── C# native (host .NET SDK) ──
echo "--- C# native ---" >&2
CS_PROJ="/tmp/sieve_cs_$TIMESTAMP"
mkdir -p "$CS_PROJ"
cat > "$CS_PROJ/sieve.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
EOF
cp sieve.cs "$CS_PROJ/Program.cs"
CS_OUTPUT=$(dotnet run -c Release --project "$CS_PROJ" -- "$LIMIT" 2>/dev/null) || CS_OUTPUT="C# native,FAILED"
echo "$CS_OUTPUT" | tee -a "$RESULTS"

# ── Poly VM ──
echo "--- Poly VM ---" >&2
POLY_BENCH="$(dirname "$0")/../../Poly.Benchmarks"
if [ -d "$POLY_BENCH" ]; then
    # Run the Poly benchmark and extract Sieve_1M mean time
    POLY_OUTPUT=$(cd "$POLY_BENCH/.." && echo "6" | dotnet run -c Release --project Poly.Benchmarks/Poly.Benchmarks.csproj 2>/dev/null)
    POLY_TIME=$(echo "$POLY_OUTPUT" | grep "Sieve_1M" | awk -F'|' '{gsub(/ /,"",$2); print $2}')
    if [ -n "$POLY_TIME" ]; then
        # Convert from ns to ms and format
        POLY_MS=$(echo "$POLY_TIME / 1000000" | bc -l 2>/dev/null || echo "$POLY_TIME")
        echo "Poly VM,$LIMIT,78498,$POLY_MS" | tee -a "$RESULTS"
    else
        echo "Poly VM,FAILED" | tee -a "$RESULTS"
    fi
else
    echo "  SKIPPED (Poly benchmarks not found)" >&2
fi

echo ""
echo "=== Results saved to $RESULTS ==="
cat "$RESULTS"
