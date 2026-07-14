#pragma once
#include <pybind11/pybind11.h>
#include <tuple>
#include <string>

namespace Glimpseon_native {
    int idle_get_milliseconds();
    int idle_get_seconds();
    bool acquire_mutex(const std::string& name);
    void release_mutex();
    int install_font(const std::string& path);
    std::tuple<int, int, pybind11::bytes> extract_icon(const std::string& path, int size = 256);
}
