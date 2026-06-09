// classlively_native/src/sys.cpp
// 空闲检测、互斥锁、字体安装

#include <pybind11/pybind11.h>
#include "sys.h"

#include <Windows.h>

namespace py = pybind11;

// 空闲检测

int classlively_native::idle_get_milliseconds() {
    LASTINPUTINFO lii = {};
    lii.cbSize = sizeof(LASTINPUTINFO);
    if (!GetLastInputInfo(&lii)) return -1;
    return static_cast<int>(GetTickCount() - lii.dwTime);
}

int classlively_native::idle_get_seconds() {
    int ms = idle_get_milliseconds();
    return (ms < 0) ? -1 : ms / 1000;
}
