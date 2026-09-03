cmake_minimum_required(VERSION 3.21)
project(vmrdp C)

# The display-only Hyper-V console client. FreeRDP does the protocol; this shim exposes exactly one thing to
# the app — frames — and links no input path at all (nothing here ever calls freerdp_input_*).
find_package(FreeRDP CONFIG REQUIRED)
find_package(WinPR CONFIG REQUIRED)

add_library(vmrdp SHARED vmrdp.c)
target_link_libraries(vmrdp PRIVATE freerdp winpr ws2_32)
target_compile_definitions(vmrdp PRIVATE WIN32_LEAN_AND_MEAN _CRT_SECURE_NO_WARNINGS)
set_target_properties(vmrdp PROPERTIES C_STANDARD 11 PREFIX "")
