# A consumer of the vcpkg port (vcpkg-registry.yml): fullint installed by vcpkg from the registry,
# found through find_package, the package test program (FullInt.Cpp/tests/package/main.cpp) run.
#   cmake -S vcpkg/test -B build -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake
# with vcpkg-configuration.json next to vcpkg.json naming the registry.
cmake_minimum_required(VERSION 3.21)
project(FullIntVcpkgTest LANGUAGES CXX)

find_package(FullInt 1.0 CONFIG REQUIRED)
message(STATUS "FullInt ${FullInt_VERSION} from ${FullInt_DIR}")

add_executable(package_test "${CMAKE_CURRENT_SOURCE_DIR}/../../FullInt.Cpp/tests/package/main.cpp")
target_link_libraries(package_test PRIVATE FullInt::FullInt)
