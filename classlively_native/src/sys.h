#pragma once
#include <pybind11/pybind11.h>

namespace classlively_native {
    // 返回系统空闲毫秒数（-1失败）
    int idle_get_milliseconds();

    // 返回系统空闲秒数（-1 失败）
    int idle_get_seconds();
}
