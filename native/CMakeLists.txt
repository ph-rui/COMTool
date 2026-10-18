cmake_minimum_required(VERSION 3.16)
project(comtool-native VERSION 0.1.0 LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_C_STANDARD 99)
set(CMAKE_AUTOMOC ON)
set(CMAKE_INCLUDE_CURRENT_DIR ON)

find_package(Qt5 5.15 REQUIRED COMPONENTS Core Widgets SerialPort)
find_package(Threads REQUIRED)

# Vendored libvterm (MIT).  It is a plain C library and has no build system
# dependency beyond the generated tables already present in the source tree.
file(GLOB VTERM_SOURCES CONFIGURE_DEPENDS
    third_party/libvterm/src/*.c)
add_library(vterm STATIC ${VTERM_SOURCES})
target_include_directories(vterm PUBLIC third_party/libvterm/include)
target_compile_definitions(vterm PRIVATE _XOPEN_SOURCE=700)
set_target_properties(vterm PROPERTIES C_STANDARD 99 C_STANDARD_REQUIRED ON)
if(MSVC)
    target_compile_options(vterm PRIVATE /W0)
else()
    target_compile_options(vterm PRIVATE -Wno-unused-parameter -Wno-sign-compare)
endif()

add_library(comtool_zmodem STATIC
    src/zmodem/codec.cpp
    src/zmodem/crc.cpp
    src/zmodem/detector.cpp
    src/zmodem/filename.cpp
    src/zmodem/reader.cpp
    src/zmodem/receiver.cpp
    src/zmodem/sender.cpp
)
target_include_directories(comtool_zmodem PUBLIC src)
target_link_libraries(comtool_zmodem PUBLIC Qt5::Core Threads::Threads)

add_executable(comtool-native
    src/appstyle.cpp
    src/main.cpp
    src/mainwindow.cpp
    src/sessiontab.cpp
    src/serialworker.cpp
    src/terminalview.cpp
    src/appconfig.cpp
    src/theme.cpp
    src/i18n.cpp
)
target_link_libraries(comtool-native PRIVATE
    Qt5::Widgets Qt5::SerialPort comtool_zmodem vterm)
if(WIN32)
    # A GUI application should not leave a console window behind, matching
    # the native terminal/Notepad feel of the rest of the UI.
    set_target_properties(comtool-native PROPERTIES WIN32_EXECUTABLE TRUE)
endif()
if(UNIX)
    target_link_libraries(comtool-native PRIVATE pthread)
endif()

enable_testing()
add_executable(zmodem_tests tests/zmodem_tests.cpp)
target_link_libraries(zmodem_tests PRIVATE comtool_zmodem Qt5::Core)
add_test(NAME zmodem_tests COMMAND zmodem_tests)

if(UNIX)
    add_executable(zmodem_peer tests/zmodem_peer.cpp)
    target_link_libraries(zmodem_peer PRIVATE comtool_zmodem Qt5::Core)
endif()

install(TARGETS comtool-native RUNTIME DESTINATION bin)
