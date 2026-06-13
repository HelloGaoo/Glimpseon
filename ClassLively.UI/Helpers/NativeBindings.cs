using System;
using System.Runtime.InteropServices;

namespace ClassLively_UI.Helpers;

/// <summary>
/// C++ Native 功能绑定
/// 导出函数列表 包括且不限于：
///   - acquire_mutex(name) → bool
///   - release_mutex() → void
///   - idle_get_milliseconds() → int
///   - set_wallpaper(path) → bool
///   - install_hook() / uninstall_hook()
///   - extract_icon(exe_path, output_path) → bool
///   - was_page_operation_recent(ms) → bool
/// </summary>
public static class NativeBindings
{
    private const string DllName = "classlively_native";

    //  单例互斥锁

    /// <summary>获取命名互斥锁（成功返回 true 已存在返回 false）</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    public static extern bool acquire_mutex(string mutex_name);

    /// <summary>释放当前持有的互斥锁</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void release_mutex();

    //  空闲检测

    /// <summary>获取系统空闲时间ms 失败返回 -1</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern int idle_get_milliseconds();

    /// <summary>最近 x ms内是否有页面操作？</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern bool was_page_operation_recent(int ms);

    //  壁纸设置

    /// <summary>设置桌面壁纸</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool set_wallpaper(string path);


    //  图标提取

    /// <summary>exe 文件提取</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool extract_icon(string exe_path, string output_path);

    //  全局钩子

    /// <summary>安装全局鼠标键盘钩子</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void install_hook();

    /// <summary>卸载全局钩子</summary>
    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
    public static extern void uninstall_hook();

    //  包装方法

    /// <summary>获取空闲时间</summary>
    public static int SafeGetIdleMs()
    {
        try { return idle_get_milliseconds(); }
        catch (DllNotFoundException) { return -1; }
        catch (Exception) { return -1; }
    }

    /// <summary>设置壁纸</summary>
    public static bool SafeSetWallpaper(string path)
    {
        try { return set_wallpaper(path); }
        catch (DllNotFoundException)
        {
            System.Diagnostics.Debug.WriteLine("[Native] 设置壁纸失败");
            return false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Native] 设置壁纸失败: {ex.Message}");
            return false;
        }
    }
}
