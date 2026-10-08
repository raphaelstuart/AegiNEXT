cmake_minimum_required(VERSION 3.25)
project(AegiNextMedia VERSION 0.1.0 LANGUAGES C CXX)

if(NOT APPLE)
    message(FATAL_ERROR "The native HDR probe supports macOS only. Build managed targets with build.ps1 -Target Managed.")
endif()

enable_language(OBJCXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)
set(CMAKE_OBJCXX_STANDARD 20)
set(CMAKE_OBJCXX_STANDARD_REQUIRED ON)
set(CMAKE_OBJCXX_EXTENSIONS OFF)
set(CMAKE_OSX_DEPLOYMENT_TARGET "14.0" CACHE STRING "Minimum macOS version")

find_package(PkgConfig REQUIRED)
file(READ "${CMAKE_CURRENT_SOURCE_DIR}/sdk-dependencies.json" SDK_MANIFEST)
set_property(DIRECTORY APPEND PROPERTY CMAKE_CONFIGURE_DEPENDS "${CMAKE_CURRENT_SOURCE_DIR}/sdk-dependencies.json")
string(JSON PLACEBO_VERSION GET "${SDK_MANIFEST}" packages libplacebo version)
unset(__pkg_config_checked_LIBPLACEBO CACHE)
pkg_check_modules(LIBPLACEBO REQUIRED IMPORTED_TARGET "libplacebo=${PLACEBO_VERSION}")
pkg_get_variable(PLACEBO_HAS_VULKAN libplacebo pl_has_vulkan)
pkg_get_variable(PLACEBO_HAS_VK_PROC_ADDR libplacebo pl_has_vk_proc_addr)
pkg_get_variable(PLACEBO_HAS_SHADERC libplacebo pl_has_shaderc)
if(NOT PLACEBO_HAS_VULKAN STREQUAL "1" OR NOT PLACEBO_HAS_VK_PROC_ADDR STREQUAL "1" OR
   NOT PLACEBO_HAS_SHADERC STREQUAL "1")
    message(FATAL_ERROR "libplacebo must support Vulkan, shaderc, and a caller-provided vkGetInstanceProcAddr.")
endif()

set(MOLTENVK_ROOT "" CACHE PATH "MoltenVK 1.4.2 installation prefix")
set(VULKAN_HEADERS_ROOT "" CACHE PATH "Vulkan-Headers 1.4.357.0 installation prefix")
unset(MOLTENVK_INCLUDE_DIR CACHE)
unset(MOLTENVK_LIBRARY CACHE)
unset(VULKAN_INCLUDE_DIR CACHE)
find_path(MOLTENVK_INCLUDE_DIR MoltenVK/mvk_private_api.h PATHS "${MOLTENVK_ROOT}/include" NO_DEFAULT_PATH REQUIRED)
find_library(MOLTENVK_LIBRARY NAMES MoltenVK PATHS "${MOLTENVK_ROOT}/lib" NO_DEFAULT_PATH REQUIRED)
find_path(VULKAN_INCLUDE_DIR vulkan/vulkan.h PATHS "${VULKAN_HEADERS_ROOT}/include" NO_DEFAULT_PATH REQUIRED)
find_library(APPKIT_FRAMEWORK AppKit REQUIRED)
find_library(QUARTZCORE_FRAMEWORK QuartzCore REQUIRED)
find_library(METAL_FRAMEWORK Metal REQUIRED)

get_filename_component(REPOSITORY_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/.." ABSOLUTE)
set(AEGINEXT_NATIVE_OUTPUT_DIR "${CMAKE_CURRENT_BINARY_DIR}/lib" CACHE PATH "Development dylib output; build.ps1 supplies the RID/configuration path")

add_library(aeginext_frame_conversion STATIC src/frame_conversion.cpp)
target_include_directories(aeginext_frame_conversion PUBLIC include src)
set_target_properties(aeginext_frame_conversion PROPERTIES POSITION_INDEPENDENT_CODE ON CXX_VISIBILITY_PRESET hidden)
target_compile_options(aeginext_frame_conversion PRIVATE -Wall -Wextra -Wpedantic)

add_library(aeginext_media SHARED src/hdr_api.mm src/hdr_context.mm src/hdr_view.mm)
target_include_directories(aeginext_media PUBLIC include PRIVATE src "${VULKAN_INCLUDE_DIR}" "${MOLTENVK_INCLUDE_DIR}")
target_compile_definitions(aeginext_media PRIVATE VK_USE_PLATFORM_METAL_EXT)
target_compile_options(aeginext_media PRIVATE -Wall -Wextra -Wpedantic -fobjc-arc -fobjc-arc-exceptions)
target_link_libraries(aeginext_media PRIVATE aeginext_frame_conversion PkgConfig::LIBPLACEBO
    "${MOLTENVK_LIBRARY}" "${APPKIT_FRAMEWORK}" "${QUARTZCORE_FRAMEWORK}" "${METAL_FRAMEWORK}")
set_target_properties(aeginext_media PROPERTIES
    OUTPUT_NAME aeginext_media
    CXX_VISIBILITY_PRESET hidden
    OBJCXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
    LIBRARY_OUTPUT_DIRECTORY "${AEGINEXT_NATIVE_OUTPUT_DIR}"
    BUILD_RPATH "${MOLTENVK_ROOT}/lib;${LIBPLACEBO_LIBRARY_DIRS}")

include(CTest)
if(BUILD_TESTING)
    add_executable(aeginext_native_tests Tests/native_tests.mm Tests/c_abi_smoke.c)
    target_include_directories(aeginext_native_tests PRIVATE include src)
    target_compile_options(aeginext_native_tests PRIVATE -Wall -Wextra -Wpedantic
        $<$<COMPILE_LANGUAGE:OBJCXX>:-fobjc-arc>)
    target_link_libraries(aeginext_native_tests PRIVATE aeginext_frame_conversion aeginext_media "${APPKIT_FRAMEWORK}")
    add_test(NAME aeginext.native.contracts COMMAND aeginext_native_tests)
endif()

message(STATUS "AegiNext development dylib: ${AEGINEXT_NATIVE_OUTPUT_DIR}/libaeginext_media.dylib")
message(STATUS "Runtime dependencies use the local package installation; this is not a release bundle.")
