#!/usr/bin/env bash
# Inside the bench container on a cloud ARM64 machine (Oracle Ampere A1, Neoverse-N1), the
# repository mounted at /src: builds the C++ library natively, runs the tests and the API
# benchmark against GMP; logs and CSVs go to /src/results/<stamp>-<compiler>/.
#
# On the machine (everything on core 1, the other core is not ours):
#   docker run --rm --cpuset-cpus=1 --memory=6g -v ~/projects/fullint:/src -w /src \
#       fullint-arm64-bench bash docker/cloud-arm64/run.sh
#
#   CXX=clang++        the compiler (default g++; the C compiler follows)
#   MCPU=neoverse-n1   -mcpu for the C++ code (default; MCPU= for none)
#   ROUNDS=3           benchmark runs (the summary takes the minimum over them)
#   OPS=add,mul,...    the benchmark's operations (default: all of them)
#   CPU=1              the core the benchmark pins itself to (the container's only one)
#   TESTS=0            skip the tests
#   SET="kara=12 ..."  thresholds for the C++ benchmark (fullint_bench_api --set, no rebuild)
#   MAX=4096           the largest size (words)
#   NET=1              also the .NET half (FullInt.Benchmarks cross: FullInt .NET and BigInteger on
#                      the same numbers) and one table of GMP / C++ / .NET / BigInteger (table.txt)
set -euo pipefail

CXX=${CXX:-g++}
if [[ "$CXX" == clang++* ]]; then CC=${CC:-clang}; else CC=${CC:-gcc}; fi
MCPU=${MCPU-neoverse-n1}
ROUNDS=${ROUNDS:-3}
OPS=${OPS:-add,sub,mul,div,mod,shl,shr,and,cmp,todec,parse}
CPU=${CPU:-1}
FLAGS=""
[[ -n "$MCPU" ]] && FLAGS="-mcpu=$MCPU"

BUILD=/src/build-cloud-$CXX
OUT=/src/results/$(date -u +%Y%m%d-%H%M%S)-$CXX
mkdir -p "$OUT"
{
    echo "== $(uname -srm), $($CXX --version | head -1), flags: -O3 $FLAGS"
    echo "== cpu: $(grep -m1 'CPU part' /proc/cpuinfo), allowed: $(grep Cpus_allowed_list /proc/self/status | cut -f2)"
    echo "== commit: $(git -C /src log --oneline -1 2>/dev/null || echo '?')"
    echo "== GMP: $(grep -m1 -E '__GNU_MP_VERSION ' /usr/include/aarch64-linux-gnu/gmp.h 2>/dev/null || echo '?')"
} | tee "$OUT/info.txt"

cmake -S /src/FullInt.Cpp -B "$BUILD" -G Ninja -DCMAKE_BUILD_TYPE=Release -DFULLINT_WITH_GMP=ON \
    -DCMAKE_CXX_COMPILER="$CXX" -DCMAKE_C_COMPILER="$CC" -DCMAKE_CXX_FLAGS="$FLAGS" > "$OUT/cmake.log"
cmake --build "$BUILD" --target fullint_arm64_kernels fullint_tests fullint_bench_api > "$OUT/build.log" 2>&1 \
    || { tail -30 "$OUT/build.log"; exit 1; }

if [[ "${TESTS:-1}" == 1 ]]; then
    echo "== tests"
    "$BUILD/fullint_arm64_kernels" | tail -1 | tee "$OUT/tests.txt"
    "$BUILD/fullint_tests" | tail -1 | tee -a "$OUT/tests.txt"
    FULLINT_CPU=baseline "$BUILD/fullint_tests" | tail -1 | tee -a "$OUT/tests.txt"
fi

MAX=${MAX:-4096}
SETS=()
for kv in ${SET:-}; do SETS+=(--set "$kv"); done
[[ -n "${SET:-}" ]] && echo "== thresholds: $SET" | tee -a "$OUT/info.txt"

echo "== benchmark: $ROUNDS rounds, core $CPU"
for r in $(seq 1 "$ROUNDS"); do
    "$BUILD/fullint_bench_api" --cpu "$CPU" --ops "$OPS" --max "$MAX" "${SETS[@]}" --csv "$OUT/bench$r.csv" > "$OUT/bench$r.txt"
    echo "   round $r done"
done

if [[ "${NET:-0}" == 1 ]]; then
    # the .NET half (writable homes for the SDK and the packages inside the container)
    export DOTNET_CLI_HOME=/src/build-cloud-dotnet/home NUGET_PACKAGES=/src/build-cloud-dotnet/nuget
    export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
    echo "== .NET: $(dotnet --version), the cross benchmark"
    dotnet build /src/FullInt.Benchmarks -c Release -f net8.0 > "$OUT/net-build.log" 2>&1 \
        || { tail -30 "$OUT/net-build.log"; exit 1; }
    dotnet run --no-build -c Release -f net8.0 --project /src/FullInt.Benchmarks -- cross --ops "$OPS" --max "$MAX" \
        --csv "$OUT/net.csv" > "$OUT/net.txt"
    echo "== GMP / C++ / .NET / BigInteger"
    "$BUILD/fullint_bench_api" --cpu "$CPU" --ops "$OPS" --max "$MAX" "${SETS[@]}" --net "$OUT/net.csv" | tee "$OUT/table.txt"
fi

# the minimum over the rounds of both columns; x = FullInt C++ / GMP (< 1: faster than GMP)
awk -F, 'FNR == 1 { next }
    { k = $1 "," $2; if (!(k in f) || $3 < f[k]) f[k] = $3; if (!(k in g) || $4 < g[k]) g[k] = $4;
      if (!(k in seen)) { seen[k] = 1; order[++n] = k } }
    END { printf "%-6s %6s %12s %12s %6s\n", "op", "words", "FullInt ns", "GMP ns", "x";
          for (i = 1; i <= n; i++) { split(order[i], a, ",");
              printf "%-6s %6s %12.1f %12.1f %6.2f\n", a[1], a[2], f[order[i]], g[order[i]], f[order[i]] / g[order[i]] } }' \
    "$OUT"/bench*.csv | tee "$OUT/summary.txt"
echo "== results: $OUT"
