cmake_minimum_required(VERSION 3.15)
project(Glimpseon_native VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

add_compile_options("/utf-8" "/openmp")  # /openmp 启用 OpenMP 并行

# 查找 Python 和 pybind11
find_package(Python 3.8 REQUIRED COMPONENTS Interpreter Development.Module)
find_package(pybind11 CONFIG REQUIRED)

pybind11_add_module(Glimpseon_native
    src/wallpaper.cpp
    src/image.cpp
    src/hook.cpp
    src/sys.cpp
)

