#!/usr/bin/env bash
# A C++ release archive (Linux, macOS): FullInt.Cpp built in Debug and Release and installed into
# one prefix (the Debug libraries with a d), then a consumer built against that prefix through
# find_package, in both configurations, and run; then the prefix packed as <name>.tar.gz.
#   bash FullInt.Cpp/tests/package/archive.sh <name> <out dir> [extra cmake arguments...]
set -euo pipefail
CPP="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
NAME=$1; OUT=$2; shift 2
mkdir -p "$OUT"; OUT="$(cd "$OUT" && pwd)"
WORK="$(mktemp -d)"
PREFIX="$WORK/$NAME"

echo "== $NAME: the library, Debug and Release"
for c in Debug Release; do
    # position-independent code: the static libraries link into shared libraries as well
    cmake -S "$CPP" -B "$WORK/lib-$c" -G Ninja -DCMAKE_BUILD_TYPE=$c -DFULLINT_BUILD_TOOLS=OFF \
        -DCMAKE_POSITION_INDEPENDENT_CODE=ON "$@" > /dev/null
    cmake --build "$WORK/lib-$c" --target fullint > /dev/null
    cmake --install "$WORK/lib-$c" --prefix "$PREFIX" > /dev/null
done
(cd "$PREFIX" && find . -type f | sort | sed 's/^/  /')

echo "== $NAME: find_package from the archive's contents"
for c in Debug Release; do
    cmake -S "$CPP/tests/package" -B "$WORK/find-$c" -G Ninja -DCMAKE_BUILD_TYPE=$c \
        -DFULLINT_CONSUME=find_package -DCMAKE_PREFIX_PATH="$PREFIX" "$@" > /dev/null
    cmake --build "$WORK/find-$c" > /dev/null
    echo -n "  $c: "; "$WORK/find-$c/package_test"
done

tar -C "$WORK" -czf "$OUT/$NAME.tar.gz" "$NAME"
echo "== $OUT/$NAME.tar.gz"
