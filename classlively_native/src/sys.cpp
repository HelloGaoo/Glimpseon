// classlively_native/src/sys.cpp
// 空闲检测、互斥锁、字体安装、图标提取

#include <pybind11/pybind11.h>
#include "sys.h"

#include <Windows.h>
#include <shellapi.h>
#include <string>
#include <vector>

#pragma comment(lib, "shell32.lib")

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

// 单例互斥锁

static HANDLE g_mutex_handle = nullptr;

bool classlively_native::acquire_mutex(const std::string& name) {
    if (g_mutex_handle) return true;  // 已持有
    std::wstring wname(name.begin(), name.end());
    g_mutex_handle = CreateMutexW(nullptr, TRUE, wname.c_str());
    if (!g_mutex_handle) return true;  // 创建失败，放行
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        CloseHandle(g_mutex_handle);
        g_mutex_handle = nullptr;
        return false;  // 已有实例
    }
    return true;  // 首次获取
}

void classlively_native::release_mutex() {
    if (g_mutex_handle) {
        ReleaseMutex(g_mutex_handle);
        CloseHandle(g_mutex_handle);
        g_mutex_handle = nullptr;
    }
}

// 字体安装

int classlively_native::install_font(const std::string& path) {
    std::wstring wpath(path.begin(), path.end());
    return AddFontResourceW(wpath.c_str());  // 返回添加的数量
}

// 图标提取

std::tuple<int, int, py::bytes> classlively_native::extract_icon(const std::string& path, int size) {
    std::wstring wpath(path.begin(), path.end());
    SHFILEINFOW sfi = {};
    SHGetFileInfoW(wpath.c_str(), 0, &sfi,sizeof(sfi), SHGFI_ICON | SHGFI_LARGEICON);
    if (!sfi.hIcon) return {0, 0, py::bytes()};

    ICONINFO ii = {};
    GetIconInfo(sfi.hIcon, &ii);

    BITMAP bm = {};
    HBITMAP hbm = ii.hbmColor ? ii.hbmColor : ii.hbmMask;
    GetObject(hbm, sizeof(BITMAP), &bm);

    int w = size > 0 ? size : bm.bmWidth;
    int h = size > 0 ? size : bm.bmHeight;

    HDC hdc = GetDC(0);
    HDC hdcMem = CreateCompatibleDC(hdc);
    HBITMAP hBmpOut = CreateCompatibleBitmap(hdc, w, h);
    HBITMAP hOld = (HBITMAP)SelectObject(hdcMem, hBmpOut);

    // 缩放绘制图标
    DrawIconEx(hdcMem, 0, 0, sfi.hIcon, w, h, 0, nullptr, DI_NORMAL);

    // 读回BGRA
    BITMAPINFOHEADER bih = {};
    bih.biSize = sizeof(BITMAPINFOHEADER);
    bih.biWidth = w;
    bih.biHeight = -h;  // 自上而下
    bih.biPlanes = 1;
    bih.biBitCount = 32;
    bih.biCompression = BI_RGB;

    std::vector<uint8_t> pixels(static_cast<size_t>(w) * h * 4);
    GetDIBits(hdcMem, hBmpOut, 0, h, pixels.data(), (BITMAPINFO*)&bih, DIB_RGB_COLORS);

    SelectObject(hdcMem, hOld);
    DeleteObject(hBmpOut);
    DeleteDC(hdcMem);
    ReleaseDC(0, hdc);

    DestroyIcon(sfi.hIcon);
    if (ii.hbmColor) DeleteObject(ii.hbmColor);
    if (ii.hbmMask) DeleteObject(ii.hbmMask);

    return {w, h, py::bytes(reinterpret_cast<const char*>(pixels.data()), pixels.size())};
}
