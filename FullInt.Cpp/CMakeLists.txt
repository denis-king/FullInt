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
# Windows x64 / x86 (NASM win64 / win32) and Linux x64 (NASM elf64, the System V entries of abi.inc);
# ARM64 (AArch64, any OS): GNU-syntax asm (Sources/arm64) over the portable C++ kernels
# (src/kernels_generic.cpp: the reference, the ..._generic names)
string(TOLOWER "${CMAKE_SYSTEM_PROCESSOR}" FULLINT_PROCESSOR)
if(FULLINT_PROCESSOR MATCHES "^(aarch64|arm64)$")
    set(FULLINT_ARCH arm64)
elseif(CMAKE_SIZEOF_VOID_P EQUAL 8)
    set(FULLINT_ARCH x64)
else()
    set(FULLINT_ARCH x86)
    # (32-bit COFF names carry a leading underscore: the sources put it, abi86.inc)
endif()

if(FULLINT_ARCH STREQUAL "arm64")
    # the AArch64 kernels (GNU syntax, assembler directives only: abi.inc) and the portable C++
    # ones under ..._generic names (kernels_generic.cpp; FULLINT_ARM64_ASM=0: C++ only, the A/B
    # reference build)
    option(FULLINT_ARM64_ASM "Build the AArch64 asm kernels (OFF: the portable C++ ones only)" ON)
    set(FULLINT_ASM_DIR "${FULLINT_ROOT}/FullInt.Asm/Sources/arm64")
    set(FULLINT_ASM_SOURCES)
    if(FULLINT_ARM64_ASM)
        enable_language(ASM)
        file(GLOB FULLINT_ASM_SOURCES CONFIGURE_DEPENDS "${FULLINT_ASM_DIR}/*.s")
    endif()
    add_library(fullint_kernels STATIC src/kernels_generic.cpp src/ntt_portable.cpp ${FULLINT_ASM_SOURCES})
    target_include_directories(fullint_kernels PUBLIC "${CMAKE_CURRENT_SOURCE_DIR}/include")
    target_compile_definitions(fullint_kernels PUBLIC FULLINT_ARM64_ASM=$<BOOL:${FULLINT_ARM64_ASM}>)
    # .include "abi.inc" (from the sources' directory); the object format for the macros there
    set(FULLINT_ASM_FLAGS "-Wa,-I${FULLINT_ASM_DIR}")
    if(APPLE)
        list(APPEND FULLINT_ASM_FLAGS "-Wa,--defsym,FULLINT_MACHO=1")
    elseif(WIN32)
        list(APPEND FULLINT_ASM_FLAGS "-Wa,--defsym,FULLINT_COFF=1")
    endif()
    if(CMAKE_ASM_COMPILER_ID MATCHES "Clang")   # (clang names every C flag a .s file does not use)
        list(APPEND FULLINT_ASM_FLAGS "-Wno-unused-command-line-argument")
    endif()
    foreach(flag IN LISTS FULLINT_ASM_FLAGS)
        target_compile_options(fullint_kernels PRIVATE "$<$<COMPILE_LANGUAGE:ASM>:${flag}>")
    endforeach()
    add_library(FullInt::kernels ALIAS fullint_kernels)
else()
if(NOT WIN32 AND NOT (CMAKE_SYSTEM_NAME STREQUAL "Linux" AND FULLINT_ARCH STREQUAL "x64"))
    message(FATAL_ERROR "FullInt.Cpp: Windows x64 / x86, Linux x64 and ARM64 so far")
endif()
if(WIN32 AND NOT CMAKE_ASM_NASM_COMPILER AND EXISTS "${FULLINT_ROOT}/Tools/nasm/nasm.exe")   # elsewhere: the system NASM
    set(CMAKE_ASM_NASM_COMPILER "${FULLINT_ROOT}/Tools/nasm/nasm.exe")
endif()
enable_language(ASM_NASM)

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
endif()

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

# the AArch64 kernels against the portable C++ ones (no GMP: quick under QEMU)
if(FULLINT_ARCH STREQUAL "arm64" AND FULLINT_ARM64_ASM)
    add_executable(fullint_arm64_kernels tests/arm64_kernels.cpp)
    find_package(Threads REQUIRED)   # (the test's watchdog)
    target_link_libraries(fullint_arm64_kernels PRIVATE FullInt::kernels Threads::Threads)
    add_executable(fullint_ntt_parts EXCLUDE_FROM_ALL bench/ntt_parts.cpp)   # the NTT kernels one by one
    target_link_libraries(fullint_ntt_parts PRIVATE FullInt::kernels)
endif()

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

    add_executable(fullint_bench_api bench/api_vs_gmp.cpp)
    target_include_directories(fullint_bench_api PRIVATE "${GMP_INCLUDE_DIR}")
    target_link_libraries(fullint_bench_api PRIVATE FullInt::fullint "${GMP_LIBRARY}")

    if(WIN32 AND FULLINT_ARCH STREQUAL "x64")   # the kernel benchmark compares 64-bit limbs with GMP's
        add_executable(fullint_bench_kernels bench/kernels_vs_gmp.cpp)
        target_include_directories(fullint_bench_kernels PRIVATE "${GMP_INCLUDE_DIR}")
        target_link_libraries(fullint_bench_kernels PRIVATE FullInt::kernels "${GMP_LIBRARY}")
    endif()
endif()
