#!/usr/bin/env bash
# Performance comparison across languages: sieve, mandelbrot, nqueens, collatz
# Uses Docker for consistent environments across platforms.
# C# native, C# vectorized, and Poly VM run from the host (no Docker needed).
set -e

cd "$(dirname "$0")"
RESULTS_DIR="./results"
mkdir -p "$RESULTS_DIR"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)
RESULTS="$RESULTS_DIR/results_$TIMESTAMP.csv"
BENCH="${1:-all}"        # which benchmark: sieve, mandelbrot, nqueens, collatz, or all
LIMIT="${2:-1000000}"    # default limit (used by sieve, collatz; ignored by mandelbrot/nqueens which are hardcoded)

echo "benchmark,language,size_or_limit,result,time_ms,prep_ms" | tee "$RESULTS"

# ── Expected results per benchmark ──
expected_for() {
    case "$1" in
        sieve)     echo "78498" ;;
        mandelbrot) echo "458080" ;;
        nqueens)   echo "92" ;;
        collatz)   echo "837799:524" ;;
        *)         echo "" ;;
    esac
}

# Validate the 3rd CSV field (result) against the expected value.
# Usage: validate_result <benchmark> <csv_line>
validate_result() {
    local bench="$1"
    local line="$2"
    local expected=$(expected_for "$bench")
    if [ -z "$expected" ]; then
        echo "  ⚠  no expected value defined for '$bench'" >&2
        return 0
    fi
    local actual
    actual=$(echo "$line" | cut -d',' -f3)
    if [ "$actual" != "$expected" ]; then
        echo "  ✗ WRONG RESULT: expected $expected, got $actual" >&2
        return 1
    fi
    echo "  ✓ result: $actual" >&2
    return 0
}

# ── Docker status check ──
if ! docker info >/dev/null 2>&1; then
    echo "ERROR: Docker is not running or not installed."
    echo "  The Docker-based languages (C, C++, Rust, Python, JS, Bun, Deno, Py+NumPy)"
    echo "  require a running Docker daemon.  C# native, C# vectorized, and Poly VM"
    echo "  benchmarks run on the host and do not need Docker."
    echo ""
    echo "  To start Docker:"
    echo "    macOS: open -a Docker"
    echo "    Linux: sudo systemctl start docker"
    echo "    WSL:   sudo service docker start"
    exit 1
fi

# ── Language definitions ──
# Each entry: label,dockerfile,source_file (with BENCHMARK prefix)
# Dockerfile.c, Dockerfile.cpp, etc. now accept SOURCE_FILE build arg

declare -a DOCKER_LANGS=(
    "C:docker/Dockerfile.c:{bench}.c"
    "C++:docker/Dockerfile.cpp:{bench}.cpp"
    "Rust:docker/Dockerfile.rust:{bench}.rs"
    "Python:docker/Dockerfile.python:{bench}.py"
    "JS:docker/Dockerfile.js:{bench}.js"
    "Bun:docker/Dockerfile.bun:{bench}.bun.js"
    "Deno:docker/Dockerfile.deno:{bench}.deno.js"
    "Py+NumPy:docker/Dockerfile.numpy:{bench}_numpy.py"
)

run_docker() {
    local bench="$1"
    local label="$2"
    local dockerfile="$3"
    local source_file="$4"
    local arg="${5:-}"

    echo "  [$bench] $label ($source_file) ..." >&2
    local img="poly-${bench}-$(echo "$label" | tr 'A-Z+' 'a-zx')"
    docker build -q -f "$dockerfile" --build-arg "SOURCE_FILE=$source_file" -t "$img" . 2>/dev/null
    local output
    if [ -n "$arg" ]; then
        output=$(docker run --rm "$img" "$arg" 2>/dev/null)
    else
        output=$(docker run --rm "$img" 2>/dev/null)
    fi
    if [ $? -ne 0 ] || [ -z "$output" ]; then
        echo "$bench,$label,FAILED" | tee -a "$RESULTS"
        return
    fi
    local first_line
    first_line=$(echo "$output" | head -1)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,$first_line" | tee -a "$RESULTS"
    else
        echo "$bench,$(echo "$first_line" | cut -d',' -f1,2),FAILED" | tee -a "$RESULTS"
    fi
}

run_cs_native() {
    local bench="$1"
    local source_file="$2"
    local arg="${3:-}"

    echo "  [$bench] C# native ..." >&2
    local tmp="/tmp/${bench}_cs_${TIMESTAMP}"
    mkdir -p "$tmp"
    cat > "$tmp/bench.csproj" << 'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
</Project>
EOF
    cp "$source_file" "$tmp/Program.cs"
    local output
    if [ -n "$arg" ]; then
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null)
    else
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" 2>/dev/null)
    fi
    if [ $? -ne 0 ] || [ -z "$output" ]; then
        echo "$bench,C# native,FAILED" | tee -a "$RESULTS"
        return
    fi
    local first_line
    first_line=$(echo "$output" | head -1)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,$first_line" | tee -a "$RESULTS"
    else
        echo "$bench,$(echo "$first_line" | cut -d',' -f1,2),FAILED" | tee -a "$RESULTS"
    fi
}

run_cs_vectorized() {
    local bench="$1"
    local source_file="$2"
    local arg="${3:-}"

    echo "  [$bench] C# vectorized ..." >&2
    local tmp="/tmp/${bench}_csv_${TIMESTAMP}"
    mkdir -p "$tmp"
    cp "$source_file" "$tmp/Program.cs"
    cat > "$tmp/bench.csproj" << ENDPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Numerics.Tensors" Version="10.0.0" />
  </ItemGroup>
</Project>
ENDPROJ
    local output
    if [ -n "$arg" ]; then
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null)
    else
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" 2>/dev/null)
    fi
    if [ $? -ne 0 ] || [ -z "$output" ]; then
        echo "$bench,C# vectorized,FAILED" | tee -a "$RESULTS"
        return
    fi
    local first_line
    first_line=$(echo "$output" | head -1)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,$first_line" | tee -a "$RESULTS"
    else
        echo "$bench,$(echo "$first_line" | cut -d',' -f1,2),FAILED" | tee -a "$RESULTS"
    fi
}

run_polyvm() {
    local bench="$1"
    local source_file="$2"
    local poly_root="$3"
    local arg="${4:-}"

    echo "  [$bench] Poly VM ..." >&2
    local tmp="/tmp/${bench}_polyvm_${TIMESTAMP}"
    mkdir -p "$tmp"
    cp "$source_file" "$tmp/Program.cs"
    cat > "$tmp/bench.csproj" << ENDPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$poly_root/Poly/Poly.csproj" />
  </ItemGroup>
</Project>
ENDPROJ
    local output
    if [ -n "$arg" ]; then
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null)
    else
        output=$(dotnet run -c Release --project "$tmp/bench.csproj" 2>/dev/null)
    fi
    if [ $? -ne 0 ] || [ -z "$output" ]; then
        echo "$bench,Poly VM,FAILED" | tee -a "$RESULTS"
        return
    fi
    local first_line
    first_line=$(echo "$output" | head -1)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,$first_line" | tee -a "$RESULTS"
    else
        echo "$bench,$(echo "$first_line" | cut -d',' -f1,2),FAILED" | tee -a "$RESULTS"
    fi
}

# ── Run a single benchmark across all languages ──
run_bench() {
    local bench="$1"
    local limit_arg="$2"
    local poly_root
    poly_root="$(cd "$(dirname "$0")/../.." && pwd)"

    case "$bench" in
        sieve)
            for lang_entry in "${DOCKER_LANGS[@]}"; do
                IFS=':' read -r label dockerfile source_pattern <<< "$lang_entry"
                source_file="${source_pattern//\{bench\}/$bench}"
                run_docker "$bench" "$label" "$dockerfile" "$source_file" "$limit_arg"
            done
            run_cs_native "$bench" "sieve.cs" "$limit_arg"
            run_cs_vectorized "$bench" "sieve_cs_vectorized.cs" "$limit_arg"
            run_polyvm "$bench" "sieve_polyvm.cs" "$poly_root" "$limit_arg"
            ;;

        mandelbrot)
            for lang_entry in "${DOCKER_LANGS[@]}"; do
                IFS=':' read -r label dockerfile source_pattern <<< "$lang_entry"
                source_file="${source_pattern//\{bench\}/$bench}"
                run_docker "$bench" "$label" "$dockerfile" "$source_file" ""
            done
            run_cs_native "$bench" "mandelbrot.cs" ""
            if [ -f "mandelbrot_cs_vectorized.cs" ]; then
                run_cs_vectorized "$bench" "mandelbrot_cs_vectorized.cs" ""
            fi
            run_polyvm "$bench" "mandelbrot_polyvm.cs" "$poly_root" "128"
            ;;

        nqueens)
            for lang_entry in "${DOCKER_LANGS[@]}"; do
                IFS=':' read -r label dockerfile source_pattern <<< "$lang_entry"
                source_file="${source_pattern//\{bench\}/$bench}"
                run_docker "$bench" "$label" "$dockerfile" "$source_file" ""
            done
            run_cs_native "$bench" "nqueens.cs" ""
            if [ -f "nqueens_cs_vectorized.cs" ]; then
                run_cs_vectorized "$bench" "nqueens_cs_vectorized.cs" ""
            fi
            run_polyvm "$bench" "nqueens_polyvm.cs" "$poly_root" "8"
            ;;

        collatz)
            for lang_entry in "${DOCKER_LANGS[@]}"; do
                IFS=':' read -r label dockerfile source_pattern <<< "$lang_entry"
                source_file="${source_pattern//\{bench\}/$bench}"
                run_docker "$bench" "$label" "$dockerfile" "$source_file" "$limit_arg"
            done
            run_cs_native "$bench" "collatz.cs" "$limit_arg"
            run_cs_vectorized "$bench" "collatz_cs_vectorized.cs" "$limit_arg"
            run_polyvm "$bench" "collatz_polyvm.cs" "$poly_root" "$limit_arg"
            ;;
    esac
}

# ── Execution ──
case "$BENCH" in
    all)
        for b in sieve mandelbrot nqueens collatz; do
            echo "" >&2
            echo "═══════════════════════════════════════════" >&2
            echo "  Benchmark: $b" >&2
            echo "═══════════════════════════════════════════" >&2
            run_bench "$b" "$LIMIT"
        done
        ;;
    sieve|mandelbrot|nqueens|collatz)
        run_bench "$BENCH" "$LIMIT"
        ;;
    *)
        echo "Usage: $0 [benchmark] [limit]"
        echo "  benchmark: sieve, mandelbrot, nqueens, collatz, all (default)"
        echo "  limit: default 1000000 (used by sieve/collatz; ignored by mandelbrot/nqueens)"
        exit 1
        ;;
esac

echo ""
echo "=== Results saved to $RESULTS ==="
column -t -s ',' "$RESULTS"
