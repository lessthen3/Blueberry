##########################################################################
#                          Blueberry v0.0.1
#         Created by Ranyodh Singh Mandur - 🫐 2026-2026
#
#              Licensed under the MIT License (MIT).
#         For more details, see the LICENSE file or visit:
#               https://opensource.org/licenses/MIT
#
#           Blueberry is a free open source text editor
##########################################################################

####################################### Config Options

option(BUILD_BLUEBERRY_TESTS "build test suite" OFF)

####################################### Enforce CMake Version

cmake_minimum_required(VERSION 4.2.0) #need this for vs2026

####################################### Project definition

project(Blueberry VERSION 0.0.1 DESCRIPTION "Blueberry" LANGUAGES C)

####################################### Enforce C17 Standard

set(CMAKE_C_STANDARD 17) #ye
set(CMAKE_C_STANDARD_REQUIRED ON)

####################################### Random Globals

set_property(GLOBAL PROPERTY USE_FOLDERS ON) #dunno where else to put this owo

if(PEACH_PLATFORM_IS_UNIX)
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

####################################### Check For No Config Set

if(NOT CMAKE_CONFIGURATION_TYPES AND NOT CMAKE_BUILD_TYPE)
    message(FATAL_ERROR "No build type specified, ending build. CMAKE WILL NOW EXIT")
endif()

####################################### Enforce 64-bit Requirement

if(NOT ${CMAKE_SIZEOF_VOID_P} EQUAL 8)
    message(FATAL_ERROR "Blueberry only supports 64-bit platforms owo.")
endif()

####################################### Find blueberry source and headers

file(
    GLOB_RECURSE 
    BLUEBERRY_SOURCES 
    CONFIGURE_DEPENDS 
    "src/*.c" 
    "src/*.h"
)

source_group(           
    TREE "${CMAKE_CURRENT_SOURCE_DIR}/src"
    PREFIX "blueberry"
    FILES ${BLUEBERRY_SOURCES}
)

####################################### Create executable target

add_executable(     
    blueberry
    ${BLUEBERRY_SOURCES}
)

target_include_directories(blueberry PRIVATE
    ${PROJECT_SOURCE_DIR}/third_party/header_only/
    ${PROJECT_SOURCE_DIR}/src
)

set_target_properties(blueberry PROPERTIES POSITION_INDEPENDENT_CODE ON) #make blueberry PIC friendly UwU

####################################### Source Includes

file(GLOB LUA_SOURCES 
    third_party/source_include/lua/*.c
)

if(PEACH_PLATFORM_IS_APPLE)
    file(GLOB PHYSFS_SOURCES 
        third_party/source_include/physfs/*.c
        third_party/source_include/physfs/*.m
    )
else()
    file(GLOB PHYSFS_SOURCES 
        third_party/source_include/physfs/*.c
    )
endif(PEACH_PLATFORM_IS_APPLE)

target_sources(blueberry PRIVATE
    ${PHYSFS_SOURCES}
    ${LUA_SOURCES}
)

# this avoids warnings from including the headers owo, WTF IS MKSTEMP LOL the c programmers urge to write the most vague name possible is strong uwu
target_include_directories(blueberry SYSTEM PRIVATE 
    ${PROJECT_SOURCE_DIR}/third_party/source_include/
)

# Suppress all warnings ;w; for third_party source files in GCC/Clang
set_source_files_properties(
    ${PHYSFS_SOURCES}
    ${LUA_SOURCES}
    PROPERTIES COMPILE_FLAGS "-w"
)

source_group("third_party/physfs" FILES ${PHYSFS_SOURCES})
source_group("third_party/lua" FILES ${LUA_SOURCES})

if(NOT PEACH_PLATFORM_IS_WINDOWS)
    target_compile_definitions(blueberry PRIVATE LUA_USE_POSIX) #so that lua uses mkstemp instead for atomic file whatevers ig
endif()

####################################### Master snuggle list for platform detection UwU (my chatgpt is bricked af)
# daddy guarantees exactly ONE of these is always defined per build so no ambiguity 🍑
# daddy guaranteed so ig it works OwO

peach_apply_platform_definitions(blueberry PRIVATE)

if(PEACH_MACOS)
    target_compile_definitions(blueberry PRIVATE
        PEACH_RENDERER_METAL
    )

elseif(PEACH_WINDOWS OR PEACH_WINDOWS_ARM64 OR PEACH_LINUX OR PEACH_BSD OR PEACH_HAIKU)
    target_compile_definitions(blueberry PRIVATE
        PEACH_RENDERER_VULKAN
        PEACH_RENDERER_OPENGL
    )
endif()

####################################### Compiler Options and Other Definitions

target_compile_definitions(blueberry PRIVATE 
    $<$<CONFIG:Debug>:BLUEBERRY_DEBUG>
    SDL_MAIN_HANDLED
)

if(MSVC)

    target_compile_options(blueberry PRIVATE
        $<$<CONFIG:Debug>:/MTd>
        $<$<OR:$<CONFIG:Release>,$<CONFIG:RelWithDebInfo>,$<CONFIG:MinSizeRel>>:/MT>
    ) 
    
    target_compile_options(blueberry PRIVATE 
        /utf-8
        /experimental:c11atomics
        /Zc:preprocessor
        /std:c17
    )

endif(MSVC)


if(PEACH_PLATFORM_IS_WINDOWS)
    target_compile_definitions(blueberry PRIVATE
        GLEW_STATIC #needed for statically linking glew
    )
endif(PEACH_PLATFORM_IS_WINDOWS)

############################################################################## DEPENDENCY LINKING OWO

####################################### helper macro for reducing repeated boiler plate for static linking uwu

macro(STATIC_IMPORT TARGET RELEASE_LIB DEBUG_LIB INCLUDE_DIR)
    add_library(${TARGET} STATIC IMPORTED)
    set_target_properties(${TARGET} PROPERTIES
        IMPORTED_LOCATION_DEBUG           ${DEBUG_LIB}
        IMPORTED_LOCATION_RELEASE         ${RELEASE_LIB}
        IMPORTED_LOCATION_RELWITHDEBINFO  ${RELEASE_LIB}
        IMPORTED_LOCATION_MINSIZEREL      ${RELEASE_LIB}
        INTERFACE_INCLUDE_DIRECTORIES     ${INCLUDE_DIR}
    )
endmacro()

####################################### Static Linkage

if(PEACH_WINDOWS)
    # AND CMAKE_SYSTEM_PROCESSOR STREQUAL "AMD64"

    ############ GL extensions for legacy systems

    STATIC_IMPORT(glew 
        "${PROJECT_SOURCE_DIR}/third_party/glew/win64/release/libglew32.lib"
        "${PROJECT_SOURCE_DIR}/third_party/glew/win64/debug/libglew32d.lib"
        "${PROJECT_SOURCE_DIR}/third_party/glew/include"
    )

    ############ Windowing and Input

    STATIC_IMPORT(SDL3 
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/win64/release/SDL3-static.lib"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/win64/debug/SDL3-static.lib"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/include"
    )

elseif(PEACH_MACOS)

    ############ Windowing and Input

    STATIC_IMPORT(SDL3 
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/steve64/release/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/steve64/debug/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/include"
    )

elseif(PEACH_LINUX) #XXX: doing these later when i compile on my linux machine, future me: WE DID BIIIITCH WE IN IT

    ############ GL extensions for legacy systems

    STATIC_IMPORT(glew 
        "${PROJECT_SOURCE_DIR}/third_party/glew/tux64/libGLEW.a"
        "${PROJECT_SOURCE_DIR}/third_party/glew/tux64/libGLEWd.a"
        "${PROJECT_SOURCE_DIR}/third_party/glew/include"
    )

    ############ Windowing and Input

    STATIC_IMPORT(SDL3 
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/tux64/release/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/tux64/debug/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/include"
    )

elseif(PEACH_BSD) #Xxx: doing this took decades off my life ;w;

    ############ GL extensions for legacy systems

    STATIC_IMPORT(glew 
        "${PROJECT_SOURCE_DIR}/third_party/glew/freebsd64/libGLEW.a"
        "${PROJECT_SOURCE_DIR}/third_party/glew/freebsd64/libGLEWd.a"
        "${PROJECT_SOURCE_DIR}/third_party/glew/include"
    )

    ############ Windowing and Input

    STATIC_IMPORT(SDL3 
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/freebsd64/release/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/freebsd64/debug/libSDL3.a"
        "${PROJECT_SOURCE_DIR}/third_party/SDL3/include"
    )

else()
    message(FATAL_ERROR "UNSUPPORTED TESTING PLATFORM BLUEBERRY IS ONLY SUPPORTED ON WINDOWS/LINUX/FREEBSD/MACOS SORRY NOT SORRY")
endif()

####################################### Dependency Linking

if(PEACH_LINUX)

    find_package(OpenGL REQUIRED)

    target_link_libraries(blueberry PRIVATE
        #opengl
        GL
        glew    
        SDL3
        m #?????????????????????????????????????????????
    )

elseif(PEACH_PLATFORM_IS_WINDOWS)

    find_package(OpenGL REQUIRED)

    target_link_libraries(blueberry PRIVATE
        #opengl
        opengl32
        glew    

        setupapi 
        version 
        imm32 
        winmm 
        kernel32

        SDL3
    )

elseif(PEACH_MACOS) #TODO fix iokit and other frameworks that dont exist on ios ipados or tvos and watchos

    target_link_libraries(blueberry PRIVATE
        "-framework AVFoundation"
        "-framework AudioToolbox"
        "-framework CoreAudio"
        "-framework CoreHaptics"
        "-framework CoreVideo"
        "-framework Metal"
        "-framework MetalKit"
        "-framework QuartzCore"
        "-framework CoreGraphics"
        "-framework Foundation"
        "-framework IOKit"
        "-framework Cocoa"
        "-framework GameController"
        "-framework CoreMedia"
        "-framework ForceFeedback"
        "-framework Carbon" #wtf sdl3 u still use this garbo
        "-framework UniformTypeIdentifiers"
        "-lpthread"
    ) #needed for statically linking SDL3

elseif(PEACH_BSD)

    # Gotta let the linker know where to find GL since bsd is a special little kitty >O<
    link_directories(/usr/local/lib)
    include_directories(/usr/local/include)

    find_package(OpenGL REQUIRED)

    target_link_libraries(blueberry PRIVATE
        GL
        glew
        usbhid # needed for SDL3
        pthread # FreeBSD uses clang by default, pthread is separate
        SDL3
    )

elseif(PEACH_WASM)
    target_link_options(blueberry PRIVATE #wasm is weird mang, but I get it owo
        "-sMAX_WEBGL_VERSION=2" 
        "-sUSE_SDL=3" 
        SDL3
    )
endif()

####################################### Executable Targets

if(BUILD_BLUEBERRY_TESTS)
    file(
        GLOB_RECURSE 
        STARLIGHT_PHYSICS_TESTS_SOURCES 
        CONFIGURE_DEPENDS 
        "tests/main.c" 
        "tests/Graphics.h"
    )

endif(BUILD_BLUEBERRY_TESTS)

####################################### Set Startup Project (Visual Studio & Xcode)

# Set blueberry as the default scheme in Xcode
if(CMAKE_GENERATOR MATCHES "Xcode")
    set(CMAKE_XCODE_GENERATE_SCHEME TRUE)
    set(CMAKE_XCODE_SCHEME_EXECUTABLE blueberry)
    message(STATUS "Setting Peach_Editor as the default startup project for Xcode")
endif()

# Set blueberry as the startup project for Visual Studio
if(CMAKE_GENERATOR MATCHES "Visual Studio")
    set_property(DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR} PROPERTY VS_STARTUP_PROJECT blueberry)
    message(STATUS "Setting blueberry as the default startup project for VS2022")
    set_property(TARGET blueberry PROPERTY VS_DEBUGGER_COMMAND_ARGUMENTS "")
endif()

