#!/usr/bin/env bash
# The C++ library as a user takes it, on Linux (the default compiler, Ninja):
#   1. builds FullInt.Cpp (Debug and Release) and installs both into $OUT/install;
#   2. builds tests/package against the install (find_package), Debug and Release, and runs them;
#   3. the same through add_subdirectory;
#   4. FETCH=1: through FetchContent of $FULLINT_GIT_REPOSITORY (default: this repository) at master.
#   bash FullInt.Cpp/tests/package/run.sh            (OUT defaults to /tmp/fullint-package)
set -euo pipefail
CPP="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
OUT="${OUT:-/tmp/fullint-package}"
rm -rf "$OUT"

echo "== the library, Debug and Release, installed"
for c in Debug Release; do
    cmake -S "$CPP" -B "$OUT/lib-$c" -G Ninja -DCMAKE_BUILD_TYPE=$c > /dev/null
    cmake --build "$OUT/lib-$c" > /dev/null
    cmake --install "$OUT/lib-$c" --prefix "$OUT/install" > /dev/null
done
(cd "$OUT/install" && find . -type f | sort | sed 's/^/  /')

run() {   # name, extra cmake arguments...
    local name=$1; shift
    for c in Debug Release; do
        cmake -S "$CPP/tests/package" -B "$OUT/$name-$c" -G Ninja -DCMAKE_BUILD_TYPE=$c "$@" > /dev/null
        cmake --build "$OUT/$name-$c" > /dev/null
        echo -n "  $c: "; "$OUT/$name-$c/package_test"
    done
}
echo "== find_package"
run find -DFULLINT_CONSUME=find_package -DCMAKE_PREFIX_PATH="$OUT/install"
echo "== add_subdirectory"
run subdir -DFULLINT_CONSUME=subdirectory
if [[ "${FETCH:-0}" == 1 ]]; then
    echo "== FetchContent"
    run fetch -DFULLINT_CONSUME=fetchcontent -DFULLINT_GIT_REPOSITORY="${FULLINT_GIT_REPOSITORY:-$(dirname "$CPP")}" -DFULLINT_GIT_TAG=master
fi
