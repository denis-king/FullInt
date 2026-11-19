cmake_minimum_required(VERSION 3.21)

# FullInt C++ library: the same asm kernels as the .NET package (FullInt.Asm/Sources),
# linked statically into the C++ binary - no DLL and no interop on a kernel call.
# The upper layer (src/, include/fullint/) is a port of FullInt.Core (C# is the master).
project(FullIntCpp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(FULLINT_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/..")

option(FULLINT_WITH_GMP "Build the tests and benchmarks against GMP (needs GMP: vcpkg feature 'gmp')" OFF)

if(MSVC)
    add_compile_options($<$<COMPILE_LANGUAGE:CXX>:/utf-8> $<$<COMPILE_LANGUAGE:CXX>:/permissive->)
endif()

# ----------------------------------------------------------------------------
# asm kernels
# ----------------------------------------------------------------------------
if(WIN32 AND NOT CMAKE_ASM_NASM_COMPILER AND EXISTS "${FULLINT_ROOT}/Tools/nasm/nasm.exe")   # elsewhere: the system NASM
    set(CMAKE_ASM_NASM_COMPILER "${FULLINT_ROOT}/Tools/nasm/nasm.exe")
endif()
enable_language(ASM_NASM)

# Windows x64 / x86 (NASM win64 / win32) and Linux x64 (NASM elf64, the System V entries of abi.inc)
if(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(FULLINT_ARCH x64)
else()
    set(FULLINT_ARCH x86)
    # (32-bit COFF names carry a leading underscore: the sources put it, abi86.inc)
endif()
if(NOT WIN32 AND NOT (CMAKE_SYSTEM_NAME STREQUAL "Linux" AND FULLINT_ARCH STREQUAL "x64"))
    message(FATAL_ERROR "FullInt.Cpp: Windows x64 / x86 and Linux x64 so far")
endif()

set(FULLINT_ASM_DIR "${FULLINT_ROOT}/FullInt.Asm/Sources/${FULLINT_ARCH}")
file(GLOB FULLINT_ASM_SOURCES CONFIGURE_DEPENDS "${FULLINT_ASM_DIR}/*.asm")

add_library(fullint_kernels STATIC ${FULLINT_ASM_SOURCES})
# archive with the C++ toolchain's librarian (CMake's ASM_NASM archive rule is the ar one)
set_target_properties(fullint_kernels PROPERTIES LINKER_LANGUAGE CXX)
target_include_directories(fullint_kernels PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")
# FULLINT_CPP: the kernels built into the C++ library (they may jump on to its C++ paths; the C#
# library's native DLL is assembled without it)
target_compile_options(fullint_kernels PRIVATE
    $<$<COMPILE_LANGUAGE:ASM_NASM>:-I${FULLINT_ASM_DIR}/>
    $<$<COMPILE_LANGUAGE:ASM_NASM>:-DFULLINT_CPP>)
if(FULLINT_ARCH STREQUAL "x86")
    # the NASM objects carry no SafeSEH table (the DLL is linked with /safeseh:no as well)
    target_link_options(fullint_kernels INTERFACE /SAFESEH:NO)
endif()
add_library(FullInt::kernels ALIAS fullint_kernels)

# ----------------------------------------------------------------------------
# the library: FullInt64 and its engine
# ----------------------------------------------------------------------------
add_library(fullint STATIC
    src/FullInt64Engine.cpp
    src/FullInt64Engine.Decimal.cpp
    src/BurnikelZiegler.cpp
    src/NewtonDivision.cpp
    src/Karatsuba.cpp
    src/Toom3.cpp
    src/Toom4.cpp
    src/Toom6.cpp
    src/ShortProduct.cpp
    src/Ntt.cpp
    src/Fft.cpp)
target_link_libraries(fullint PUBLIC FullInt::kernels)
add_library(FullInt::fullint ALIAS fullint)

add_executable(fullint_kara_tune EXCLUDE_FROM_ALL bench/kara_tune.cpp)
target_link_libraries(fullint_kara_tune PRIVATE FullInt::fullint)

# ----------------------------------------------------------------------------
# tests and benchmarks (GMP is the reference)
# ----------------------------------------------------------------------------
if(FULLINT_WITH_GMP)
    find_path(GMP_INCLUDE_DIR gmp.h REQUIRED)
    find_library(GMP_LIBRARY NAMES gmp libgmp REQUIRED)
    find_library(GMPXX_LIBRARY NAMES gmpxx libgmpxx)

    enable_testing()
    add_executable(fullint_tests tests/fullint_tests.cpp)
    target_include_directories(fullint_tests PRIVATE "${GMP_INCLUDE_DIR}")
    target_link_libraries(fullint_tests PRIVATE FullInt::fullint "${GMP_LIBRARY}")
    add_test(NAME fullint_tests COMMAND fullint_tests)

    if(WIN32)   # the benchmarks: Windows so far (windows.h: the process priority, the timers)
    add_executable(fullint_bench_api bench/api_vs_gmp.cpp)
    target_include_directories(fullint_bench_api PRIVATE "${GMP_INCLUDE_DIR}")
    target_link_libraries(fullint_bench_api PRIVATE FullInt::fullint "${GMP_LIBRARY}")
    endif()

    if(WIN32 AND FULLINT_ARCH STREQUAL "x64")   # the kernel benchmark compares 64-bit limbs with GMP's
        add_executable(fullint_bench_kernels bench/kernels_vs_gmp.cpp)
        target_include_directories(fullint_bench_kernels PRIVATE "${GMP_INCLUDE_DIR}")
        target_link_libraries(fullint_bench_kernels PRIVATE FullInt::kernels "${GMP_LIBRARY}")
    endif()
endif()
