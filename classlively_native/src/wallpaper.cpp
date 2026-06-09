// classlively_native/src/wallpaper.cpp

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

PYBIND11_MODULE(classlively_native, m) {
    m.doc() = "ClassLively native C++ extensions (system layer)";

    m.def("set_wallpaper", &set_wallpaper,
          "Set desktop wallpaper by file path. Returns True on success.",
          py::arg("path"));

    m.def("blur_image", &classlively_native::blur_image_py,
          "OpenMP parallel Gaussian blur on BGRA raw pixels. Returns blurred bytes.\n"
          "Args: input(bytes-like, BGRA), width(int), height(int), radius(int 0-30)\n"
          "Returns: bytes of blurred BGRA pixel data",
          py::arg("input"), py::arg("width"), py::arg("height"), py::arg("radius"));

    // 全局钩子
    m.def("install_hook", &classlively_native::install_hook,
          "Install global low-level keyboard (PageUp/Down) and mouse (wheel) hooks.");
    m.def("uninstall_hook", &classlively_native::uninstall_hook,
          "Uninstall all global hooks.");
    m.def("was_page_operation_recent", &classlively_native::was_page_operation_recent,
          "Check if PageUp/Down or mouse wheel event occurred within ms_threshold milliseconds.\n"
          "Args: ms_threshold(int)\nReturns: bool",
          py::arg("ms_threshold"));

    // 系统工具
    m.def("idle_get_milliseconds", &classlively_native::idle_get_milliseconds,
          "Get system idle time in milliseconds. Returns -1 on failure.");
    m.def("idle_get_seconds", &classlively_native::idle_get_seconds,
          "Get system idle time in seconds. Returns -1 on failure.");
}
