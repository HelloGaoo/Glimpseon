// Glimpseon_native/src/hook.cpp
// 检测 PageUp/PageDown 按键和鼠标滚轮
//
// API：
//   install_hook()              → 安装钩子
//   uninstall_hook()            → 卸载钩子
//   was_page_operation_recent(ms) → 最近 ms内是否有翻页/滚轮操作

#include <pybind11/pybind11.h>
#include "hook.h"

#include <windows.h>
#include <cstdio>

namespace py = pybind11;

static HHOOK g_keyboard_hook = nullptr;
static HHOOK g_mouse_hook    = nullptr;
static ULONGLONG g_last_page_tick = 0;  // GetTickCount64() 时间戳

// 键盘
static LRESULT CALLBACK keyboard_proc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode >= 0) {
        KBDLLHOOKSTRUCT* kb = reinterpret_cast<KBDLLHOOKSTRUCT*>(lParam);
        // VK_PRIOR=33(PageUp), VK_NEXT=34(PageDown)
        if (kb->vkCode == VK_PRIOR || kb->vkCode == VK_NEXT) {
            g_last_page_tick = GetTickCount64();
        }
    }
    return CallNextHookEx(g_keyboard_hook, nCode, wParam, lParam);
}

// 鼠标
static LRESULT CALLBACK mouse_proc(int nCode, WPARAM wParam, LPARAM lParam) {
    if (nCode >= 0 && wParam == WM_MOUSEWHEEL) {
        g_last_page_tick = GetTickCount64();
    }
    return CallNextHookEx(g_mouse_hook, nCode, wParam, lParam);
}


void Glimpseon_native::install_hook() {
    if (g_keyboard_hook || g_mouse_hook) return;  // 已安装

    HMODULE hMod = GetModuleHandleW(nullptr);

    g_keyboard_hook = SetWindowsHookExW(
        WH_KEYBOARD_LL, keyboard_proc, hMod, 0
    );
    if (!g_keyboard_hook) {
        fprintf(stderr, "[native] 键盘钩子安装失败: %lu\n", GetLastError());
    }

    g_mouse_hook = SetWindowsHookExW(
        WH_MOUSE_LL, mouse_proc, hMod, 0
    );
    if (!g_mouse_hook) {
        fprintf(stderr, "[native] 鼠标钩子安装失败: %lu\n", GetLastError());
    }

    g_last_page_tick = GetTickCount64();
}

void Glimpseon_native::uninstall_hook() {
    if (g_keyboard_hook) {
        UnhookWindowsHookEx(g_keyboard_hook);
        g_keyboard_hook = nullptr;
    }
    if (g_mouse_hook) {
        UnhookWindowsHookEx(g_mouse_hook);
        g_mouse_hook = nullptr;
    }
}

bool Glimpseon_native::was_page_operation_recent(int ms_threshold) {
    if (ms_threshold <= 0) return false;
    ULONGLONG now = GetTickCount64();
    // 处理 GetTickCount64 49.7 天溢出
    return (now - g_last_page_tick) < static_cast<ULONGLONG>(ms_threshold);
}
