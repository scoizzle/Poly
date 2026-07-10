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
ITERATIONS="${3:-5}"     # number of runs per implementation for aggregated stats
WARMUP="${4:-1}"         # number of warmup runs before timing (discarded)

echo "benchmark,language,size_or_limit,result,runs,min_us,max_us,avg_us,prep_ms" | tee "$RESULTS"

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

# ── Multi-run helper ──────────────────────────────────────────
# Runs a command N times and extracts time_ms from its CSV output (field 4).
# Returns: "min max avg first_line"
# The first_line is used for result validation.
# Usage: run_and_collect <setup_cmd> <run_cmd> [has_prep]
#   setup_cmd:   command that prints one CSV output line (warmup, discarded)
#   run_cmd:     command that prints one CSV output line (timed)
#   has_prep:    if 1, the CSV has a prep_ms field (field 5)
run_and_collect() {
    local run_cmd="$1"
    local has_prep="${2:-0}"

    local times=()
    local first_line=""

    # Warmup
    local w
    for ((w = 0; w < WARMUP; w++)); do
        eval "$run_cmd" >/dev/null 2>&1 || true
    done

    # Timed runs
    local i
    for ((i = 0; i < ITERATIONS; i++)); do
        local line
        line=$(eval "$run_cmd" 2>/dev/null | head -1)
        if [ -z "$line" ]; then
            echo "  ⚠  run $i produced no output" >&2
            continue
        fi
        if [ -z "$first_line" ]; then
            first_line="$line"
        fi
        local t
        t=$(echo "$line" | cut -d',' -f4)
        times+=("$t")
    done

    if [ ${#times[@]} -eq 0 ]; then
        echo "  ✗ all runs failed" >&2
        return 1
    fi

    # Sort and compute stats
    local sorted
    sorted=($(printf '%s\n' "${times[@]}" | sort -n))
    local min="${sorted[0]}"
    local max="${sorted[${#sorted[@]}-1]}"
    local sum=0
    for t in "${times[@]}"; do
        sum=$(awk "BEGIN {print $sum + $t}")
    done
    local avg
    avg=$(awk "BEGIN {printf \"%.3f\", $sum / ${#times[@]}}")
    local runs="${#times[@]}"

    echo "$min $max $avg $first_line"
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
    # Get result from first (warmup) run
    local warmup_line
    if [ -n "$arg" ]; then
        warmup_line=$(docker run --rm "$img" "$arg" 2>/dev/null | head -1)
    else
        warmup_line=$(docker run --rm "$img" 2>/dev/null | head -1)
    fi
    if [ -z "$warmup_line" ]; then
        echo "$bench,$label,FAILED" | tee -a "$RESULTS"
        return
    fi

    # Multi-run
    local times=()
    local i
    for ((i = 0; i < ITERATIONS + WARMUP; i++)); do
        local line
        if [ -n "$arg" ]; then
            line=$(docker run --rm "$img" "$arg" 2>/dev/null | head -1)
        else
            line=$(docker run --rm "$img" 2>/dev/null | head -1)
        fi
        if [ -z "$line" ]; then continue; fi
        # First ITERATIONS runs are warmup (if WARMUP > 0)
        if ((i >= WARMUP)); then
            local t; t=$(echo "$line" | cut -d',' -f4)
            times+=("$t")
        fi
    done

    if [ ${#times[@]} -eq 0 ]; then
        echo "$bench,$label,FAILED" | tee -a "$RESULTS"
        return
    fi
    local sorted; sorted=($(printf '%s\n' "${times[@]}" | sort -n))
    local min="${sorted[0]}" max="${sorted[${#sorted[@]}-1]}" sum=0
    for t in "${times[@]}"; do sum=$(awk "BEGIN {print $sum + $t}"); done
    local avg; avg=$(awk "BEGIN {printf \"%.3f\", $sum / ${#times[@]}}")

    local size result
    result=$(echo "$warmup_line" | cut -d',' -f3)
    size=$(echo "$warmup_line" | cut -d',' -f2)
    if validate_result "$bench" "$warmup_line"; then
        echo "$bench,$label,$size,$result,$ITERATIONS,$min,$max,$avg,0" | tee -a "$RESULTS"
    else
        echo "$bench,$label,FAILED" | tee -a "$RESULTS"
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
    # Build once before timing runs
    dotnet build -c Release "$tmp/bench.csproj" >/dev/null 2>&1 || return 1
    local run_cmd="dotnet run -c Release --project $tmp/bench.csproj -- $arg"
    local stats
    stats=$(run_and_collect "$run_cmd" 0) || {
        echo "$bench,C# native,FAILED" | tee -a "$RESULTS"
        return
    }
    local min max avg first_line
    read -r min max avg first_line <<< "$stats"
    local size result
    result=$(echo "$first_line" | cut -d',' -f3)
    size=$(echo "$first_line" | cut -d',' -f2)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,C# native,$size,$result,$ITERATIONS,$min,$max,$avg,0" | tee -a "$RESULTS"
    else
        echo "$bench,C# native,FAILED" | tee -a "$RESULTS"
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
    dotnet build -c Release "$tmp/bench.csproj" >/dev/null 2>&1 || return 1
    local run_cmd="dotnet run -c Release --project $tmp/bench.csproj -- $arg"
    local stats
    stats=$(run_and_collect "$run_cmd" 0) || {
        echo "$bench,C# vectorized,FAILED" | tee -a "$RESULTS"
        return
    }
    local min max avg first_line
    read -r min max avg first_line <<< "$stats"
    local size result
    result=$(echo "$first_line" | cut -d',' -f3)
    size=$(echo "$first_line" | cut -d',' -f2)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,C# vectorized,$size,$result,$ITERATIONS,$min,$max,$avg,0" | tee -a "$RESULTS"
    else
        echo "$bench,C# vectorized,FAILED" | tee -a "$RESULTS"
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
    # Build once, then capture prep_ms from the first run
    dotnet build -c Release "$tmp/bench.csproj" >/dev/null 2>&1 || return 1
    local first_line
    first_line=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null | head -1)
    if [ -z "$first_line" ]; then
        echo "$bench,Poly VM,FAILED" | tee -a "$RESULTS"
        return
    fi
    local prep_ms
    prep_ms=$(echo "$first_line" | cut -d',' -f5)

    # Multi-run for execution time (build cache avoids recompilation)
    local run_cmd="dotnet run -c Release --no-build --project $tmp/bench.csproj -- $arg"
    local stats
    stats=$(run_and_collect "$run_cmd" 1) || {
        echo "$bench,Poly VM,FAILED" | tee -a "$RESULTS"
        return
    }
    local min max avg
    read -r min max avg first_line <<< "$stats"
    local size result
    result=$(echo "$first_line" | cut -d',' -f3)
    size=$(echo "$first_line" | cut -d',' -f2)
    if validate_result "$bench" "$first_line"; then
        echo "$bench,Poly VM,$size,$result,$ITERATIONS,$min,$max,$avg,$prep_ms" | tee -a "$RESULTS"
    else
        echo "$bench,Poly VM,FAILED" | tee -a "$RESULTS"
    fi
}

# Run a Poly VM benchmark in Normal (debug) compilation mode.
# Copies the source, replaces NoDebug with Normal, builds, benchmarks.
run_polyvm_normal() {
    local bench="$1"
    local source_file="$2"
    local poly_root="$3"
    local arg="${4:-}"

    echo "  [$bench] Poly VM (Normal) ..." >&2
    local tmp="/tmp/${bench}_polyvm_normal_${TIMESTAMP}"
    mkdir -p "$tmp"
    sed 's/CompilationMode\.NoDebug/CompilationMode.Normal/' "$source_file" > "$tmp/Program.cs"
    cat > "$tmp/bench.csproj" << ENDPROJ
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$poly_root/Poly/Poly.csproj" />
  </ItemGroup>
</Project>
ENDPROJ
    dotnet build -c Release "$tmp/bench.csproj" >/dev/null 2>&1 || return 1
    local first_line
    first_line=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null | head -1)
    if [ -z "$first_line" ]; then
        echo "$bench,Poly VM (Normal),FAILED" | tee -a "$RESULTS"
        return
    fi
    local prep_ms
    prep_ms=$(echo "$first_line" | cut -d',' -f5)

    local run_cmd="dotnet run -c Release --no-build --project $tmp/bench.csproj -- $arg"
    local stats
    stats=$(run_and_collect "$run_cmd" 1) || {
        echo "$bench,Poly VM (Normal),FAILED" | tee -a "$RESULTS"
        return
    }
    local min max avg
    read -r min max avg first_line <<< "$stats"
    local size result
    result=$(echo "$first_line" | cut -d',' -f3)
    size=$(echo "$first_line" | cut -d',' -f2)
    echo "$bench,Poly VM (Normal),$size,$result,$ITERATIONS,$min,$max,$avg,$prep_ms" | tee -a "$RESULTS"
}

# Run a Poly VM variant that is NOT validated against the fixed-point expected
# result (e.g. double-precision mandelbrot). Source must print:
#   language,size,result,us,prep_ms
# same field layout as the other Poly drivers.
run_polyvm_variant() {
    local bench="$1"
    local source_file="$2"
    local poly_root="$3"
    local arg="${4:-}"
    local display_name="${5:-}"   # optional override for log line

    if [ ! -f "$source_file" ]; then
        echo "  ⚠  skip missing $source_file" >&2
        return
    fi

    local tag
    tag=$(basename "$source_file" .cs)
    echo "  [$bench] Poly variant ($tag)${display_name:+ — $display_name} ..." >&2
    local tmp="/tmp/${bench}_${tag}_${TIMESTAMP}"
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
    dotnet build -c Release "$tmp/bench.csproj" >/dev/null 2>&1 || {
        echo "$bench,$tag,FAILED" | tee -a "$RESULTS"
        return
    }
    local first_line
    first_line=$(dotnet run -c Release --project "$tmp/bench.csproj" -- "$arg" 2>/dev/null | head -1)
    if [ -z "$first_line" ]; then
        echo "$bench,$tag,FAILED" | tee -a "$RESULTS"
        return
    fi
    local prep_ms language size result
    language=$(echo "$first_line" | cut -d',' -f1)
    size=$(echo "$first_line" | cut -d',' -f2)
    result=$(echo "$first_line" | cut -d',' -f3)
    prep_ms=$(echo "$first_line" | cut -d',' -f5)

    local run_cmd="dotnet run -c Release --no-build --project $tmp/bench.csproj -- $arg"
    local stats
    stats=$(run_and_collect "$run_cmd" 1) || {
        echo "$bench,$language,FAILED" | tee -a "$RESULTS"
        return
    }
    local min max avg
    read -r min max avg first_line <<< "$stats"
    # Re-parse size/result from a timed line in case they differ (they shouldn't)
    size=$(echo "$first_line" | cut -d',' -f2)
    result=$(echo "$first_line" | cut -d',' -f3)
    language=$(echo "$first_line" | cut -d',' -f1)
    echo "$bench,$language,$size,$result,$ITERATIONS,$min,$max,$avg,$prep_ms" | tee -a "$RESULTS"
}

# ── Host-side C# / Poly variants for one bench ──
# Runs every on-disk variant that exists; skips silently when a file is absent.
run_host_variants() {
    local bench="$1"
    local poly_root="$2"
    local arg="${3:-}"          # size/limit passed to the program
    local poly_arg="${4:-$arg}" # arg for Poly drivers (may differ, e.g. mandelbrot 128)

    # C# native
    if [ -f "${bench}.cs" ]; then
        run_cs_native "$bench" "${bench}.cs" "$arg"
    fi

    # C# vectorized
    if [ -f "${bench}_cs_vectorized.cs" ]; then
        run_cs_vectorized "$bench" "${bench}_cs_vectorized.cs" "$arg"
    fi

    # Poly VM (NoDebug) + Normal mode
    if [ -f "${bench}_polyvm.cs" ]; then
        run_polyvm "$bench" "${bench}_polyvm.cs" "$poly_root" "$poly_arg"
        run_polyvm_normal "$bench" "${bench}_polyvm.cs" "$poly_root" "$poly_arg"
    fi

    # Extra Poly variants (double-precision, alternate lowerings, …)
    # Any ${bench}_polyvm_*.cs other than the main ${bench}_polyvm.cs
    local variant
    local variants=( "${bench}"_polyvm_*.cs )
    for variant in "${variants[@]}"; do
        # When the glob matches nothing, bash leaves the pattern as a literal.
        [ -f "$variant" ] || continue
        run_polyvm_variant "$bench" "$variant" "$poly_root" "$poly_arg"
    done
}

# ── Run a single benchmark across all languages ──
run_bench() {
    local bench="$1"
    local limit_arg="$2"
    local poly_root
    poly_root="$(cd "$(dirname "$0")/../.." && pwd)"

    # Docker-based languages (skip missing sources)
    local lang_entry label dockerfile source_pattern source_file docker_arg
    docker_arg="$limit_arg"
    case "$bench" in
        mandelbrot|nqueens) docker_arg="" ;;
    esac

    for lang_entry in "${DOCKER_LANGS[@]}"; do
        IFS=':' read -r label dockerfile source_pattern <<< "$lang_entry"
        source_file="${source_pattern//\{bench\}/$bench}"
        if [ ! -f "$source_file" ]; then
            echo "  ⚠  skip missing $source_file ($label)" >&2
            continue
        fi
        run_docker "$bench" "$label" "$dockerfile" "$source_file" "$docker_arg"
    done

    # Host-side C# + Poly (and any polyvm_* variants on disk)
    case "$bench" in
        sieve)
            run_host_variants "$bench" "$poly_root" "$limit_arg" "$limit_arg"
            ;;
        mandelbrot)
            run_host_variants "$bench" "$poly_root" "" "128"
            ;;
        nqueens)
            run_host_variants "$bench" "$poly_root" "" "8"
            ;;
        collatz)
            run_host_variants "$bench" "$poly_root" "$limit_arg" "$limit_arg"
            ;;
        *)
            echo "  ✗ unknown bench: $bench" >&2
            return 1
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
