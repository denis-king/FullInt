# Using the C++ library

`fullnumerics::FullInt` (header `<fullnumerics/FullInt.h>`) is a static library: the C++ upper layer plus the assembly kernels.

| Platform | Kernels | Build needs |
|---|---|---|
| Windows x64, x86 | NASM | MSVC; NASM is bundled in `Tools/nasm` |
| Linux x64 | NASM elf64 | GCC or Clang, `nasm` |
| Linux ARM64 | AArch64 GNU assembly | GCC or Clang |
| Linux ARMv7 (armhf) | ARMv7 GNU assembly (the NTT on NEON when the CPU has it) | GCC or Clang |

All platforms need C++20 and CMake 3.21 or later.

Every way below gives the same target: link `FullInt::FullInt`, and the include path, the kernels and the link flags come with it.

```cmake
target_link_libraries(app PRIVATE FullInt::FullInt)
```

```cpp
#include <fullnumerics/FullInt.h>
using fullnumerics::FullInt;

FullInt x = fullnumerics::Pow(FullInt(3), 1000) * FullInt::Parse("123456789012345678901234567890");
std::cout << x.ToString() << "\n";
```

## 1. Install, then `find_package`

Build and install once:

```sh
cmake -S FullInt.Cpp -B build -G "Ninja Multi-Config"
cmake --build build --config Release
cmake --build build --config Debug
cmake --install build --config Release --prefix <dir>
cmake --install build --config Debug   --prefix <dir>
```

The install directory then holds:

```
<dir>/include/fullnumerics/*.h
<dir>/lib/fullint.lib, fullint_kernels.lib             (Linux: libfullint.a, libfullint_kernels.a)
<dir>/lib/fullintd.lib, fullint_kernelsd.lib           (Debug)
<dir>/lib/cmake/FullInt/FullIntConfig.cmake, …Version.cmake, FullIntTargets*.cmake
```

In the user's project, with `CMAKE_PREFIX_PATH=<dir>`:

```cmake
find_package(FullInt 1.0 CONFIG REQUIRED)
target_link_libraries(app PRIVATE FullInt::FullInt)
```

The installed files work as follows:
- **Debug and Release.** The two sit side by side; Debug has the `d` suffix. MSVC's Debug and Release runtimes do not mix, and CMake picks the right one for each configuration.
- **Version.** Any 1.x satisfies `find_package(FullInt 1.0)`.
- **Architecture.** A 32-bit install is not offered to a 64-bit project.
- **Runtime.** The library is built with the default MSVC runtime (`/MD`). A project with `/MT` builds it with the same `CMAKE_MSVC_RUNTIME_LIBRARY`.

## 2. From the sources: `add_subdirectory` or `FetchContent`

```cmake
add_subdirectory(third_party/FullInt/FullInt.Cpp)
```

```cmake
include(FetchContent)
FetchContent_Declare(FullInt
    GIT_REPOSITORY https://github.com/denis-king/FullInt
    GIT_TAG        master            # a release tag once there is one
    GIT_SHALLOW    TRUE
    SOURCE_SUBDIR  FullInt.Cpp)
FetchContent_MakeAvailable(FullInt)
```

As a subproject it builds the library only: no tests, no benchmarks, no install rules. `FULLINT_INSTALL=ON` adds the install rules when the parent project installs it.

## Options

| Option | Default | Effect |
|---|---|---|
| `FULLINT_INSTALL` | ON when top level | The install rules and the `find_package` files |
| `FULLINT_BUILD_TOOLS` | ON when top level | The ARM kernel tests (ARM64, ARMv7) and probes |
| `FULLINT_WITH_GMP` | OFF | The tests and benchmarks against GMP (needs GMP, e.g. the vcpkg feature `gmp`) |
| `FULLINT_ARM64_ASM` | ON | ARM64: the assembly kernels (OFF: the portable C++ ones) |
| `FULLINT_ARM32_ASM` | ON | ARMv7: the assembly kernels (OFF: the portable C++ ones) |

## Headers

Every header in `include/fullnumerics` is installed, because `FullInt.h` includes the core (`FullIntCore.h`, `kernels.h`, `Cpu.h`, `FullHelper.h`). The API is `FullInt.h`; the others are internal and may change.

## Checking it

`FullInt.Cpp/tests/package` is a consumer project. It builds one program each way and checks known values and arithmetic identities on numbers up to 30000 words.

```powershell
powershell -File FullInt.Cpp/tests/package/run.ps1 -Arch x64 [-Fetch]   # Windows; -Arch x86
```

```sh
bash FullInt.Cpp/tests/package/run.sh                                    # Linux; FETCH=1 adds FetchContent
```

Results on 2026-11-26, version 1.0.0:
- Windows x64 and x86 (MSVC): `find_package` in Debug and Release, and `add_subdirectory`, all passed.
- Linux x64 (GCC 13, Docker) and Linux ARM64 (Neoverse-N1, GCC): the same, all passed.
