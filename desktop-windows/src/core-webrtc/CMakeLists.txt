cmake_minimum_required(VERSION 3.20)
project(core_webrtc LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

option(P2PAUDIO_USE_LIBDATACHANNEL "Enable libdatachannel backend" ON)

if(P2PAUDIO_USE_LIBDATACHANNEL)
    find_package(LibDataChannel CONFIG QUIET)
endif()

add_library(p2paudio_core_webrtc SHARED
    src/session_controller.cpp
    src/core_webrtc_exports.cpp
)

target_include_directories(p2paudio_core_webrtc PUBLIC
    ${CMAKE_CURRENT_SOURCE_DIR}/include
)

if(P2PAUDIO_USE_LIBDATACHANNEL AND TARGET LibDataChannel::LibDataChannel)
    target_compile_definitions(p2paudio_core_webrtc PRIVATE P2PAUDIO_HAS_LIBDATACHANNEL=1)
    target_link_libraries(p2paudio_core_webrtc PRIVATE LibDataChannel::LibDataChannel)
else()
    target_compile_definitions(p2paudio_core_webrtc PRIVATE P2PAUDIO_HAS_LIBDATACHANNEL=0)
endif()

set_target_properties(p2paudio_core_webrtc PROPERTIES
    OUTPUT_NAME "p2paudio_core_webrtc"
    WINDOWS_EXPORT_ALL_SYMBOLS OFF
)
