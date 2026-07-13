// Glimpseon_native/src/wallpaper.cpp

#include <pybind11/pybind11.h>
#include <Windows.h>
#include "image_blur.h"
#include "hook.h"
#include "sys.h"

namespace py = pybind11;

/// 设置桌面壁纸
///
/// \param path
/// \return
///
/// 原：
///   ctypes.windll.user32.SystemParametersInfoW(
///       20, 0, path, 0x01 | 0x02
///   )
bool set_wallpaper(const std::string& path) {
    UINT result = SystemParametersInfoA(
        SPI_SETDESKWALLPAPER,           // uiAction = 20
        0,                              // uiParam (unused)
        (PVOID)path.c_str(),            // pvParam = 文件路径
        SPIF_UPDATEINIFILE | SPIF_SENDCHANGE  // fWinIni = 持久化 + 广播
    );
    return result != 0;
}

PYBIND11_MODULE(Glimpseon_native, m) {
    m.doc() = "Glimpseon native C++ extensions (system layer)";

    m.def("set_wallpaper", &set_wallpaper,
          "Set desktop wallpaper by file path. Returns True on success.",
          py::arg("path"));

    m.def("blur_image", &Glimpseon_native::blur_image_py,
          "OpenMP parallel Gaussian blur on BGRA raw pixels. Returns blurred bytes.\n"
          "Args: input(bytes-like, BGRA), width(int), height(int), radius(int 0-30)\n"
          "Returns: bytes of blurred BGRA pixel data",
          py::arg("input"), py::arg("width"), py::arg("height"), py::arg("radius"));

    // 全局钩子
    m.def("install_hook", &Glimpseon_native::install_hook,
          "Install global low-level keyboard (PageUp/Down) and mouse (wheel) hooks.");
    m.def("uninstall_hook", &Glimpseon_native::uninstall_hook,
          "Uninstall all global hooks.");
    m.def("was_page_operation_recent", &Glimpseon_native::was_page_operation_recent,
          "Check if PageUp/Down or mouse wheel event occurred within ms_threshold milliseconds.\n"
          "Args: ms_threshold(int)\nReturns: bool",
          py::arg("ms_threshold"));

    // 系统工具
    m.def("idle_get_milliseconds", &Glimpseon_native::idle_get_milliseconds,
          "Get system idle time in milliseconds. Returns -1 on failure.");
    m.def("idle_get_seconds", &Glimpseon_native::idle_get_seconds,
          "Get system idle time in seconds. Returns -1 on failure.");

    // 互斥锁
    m.def("acquire_mutex", &Glimpseon_native::acquire_mutex,
          "Acquire named mutex. Returns True if acquired (first instance), False if already exists.",
          py::arg("name"));
    m.def("release_mutex", &Glimpseon_native::release_mutex,
          "Release the mutex.");

    // 字体安装
    m.def("install_font", &Glimpseon_native::install_font,
          "Install font to system. Returns number of fonts added.",
          py::arg("path"));

    // 图标提取
    m.def("extract_icon", &Glimpseon_native::extract_icon,
          "Extract icon from exe/dll file. Returns (width, height, bgra_bytes).",
          py::arg("path"), py::arg("size") = 256);
}
