# A consumer of the FullInt C++ library (tests/package/run.ps1, run.sh): the same program built
# three ways, as a user would take the library.
#   -DFULLINT_CONSUME=find_package   after cmake --install (CMAKE_PREFIX_PATH = the install prefix)
#   -DFULLINT_CONSUME=subdirectory   add_subdirectory(<repo>/FullInt.Cpp)
#   -DFULLINT_CONSUME=fetchcontent   FetchContent of the repository (FULLINT_GIT_REPOSITORY / _TAG;
#                                    a local path or URL), SOURCE_SUBDIR FullInt.Cpp
cmake_minimum_required(VERSION 3.21)
project(FullIntPackageTest LANGUAGES CXX)

set(FULLINT_CONSUME "find_package" CACHE STRING "find_package, subdirectory or fetchcontent")

if(FULLINT_CONSUME STREQUAL "find_package")
    find_package(FullInt 1.0 CONFIG REQUIRED)
    message(STATUS "FullInt ${FullInt_VERSION} from ${FullInt_DIR}")
elseif(FULLINT_CONSUME STREQUAL "subdirectory")
    add_subdirectory("${CMAKE_CURRENT_SOURCE_DIR}/../.." fullint)
elseif(FULLINT_CONSUME STREQUAL "fetchcontent")
    include(FetchContent)
    set(FULLINT_GIT_REPOSITORY "https://github.com/denis-king/FullInt" CACHE STRING "")
    set(FULLINT_GIT_TAG "master" CACHE STRING "")
    FetchContent_Declare(FullInt
        GIT_REPOSITORY "${FULLINT_GIT_REPOSITORY}"
        GIT_TAG "${FULLINT_GIT_TAG}"
        GIT_SHALLOW TRUE
        SOURCE_SUBDIR FullInt.Cpp)
    FetchContent_MakeAvailable(FullInt)
else()
    message(FATAL_ERROR "FULLINT_CONSUME: find_package, subdirectory or fetchcontent")
endif()

add_executable(package_test main.cpp)
target_link_libraries(package_test PRIVATE FullInt::FullInt)
