cmake_minimum_required(VERSION 3.16)
project(ctg_server CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_executable(ctg_gameserver
    src/server/main.cpp
    src/server/GameServer.cpp
    src/net/UdpSocket.cpp)

target_compile_options(ctg_gameserver PRIVATE -Wall -Wextra)
